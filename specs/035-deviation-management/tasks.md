# Tasks: Deviation Management

**Input**: Design documents from `/specs/035-deviation-management/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mcp-tools.md, contracts/api-endpoints.md, quickstart.md

**Tests**: Included per Constitution Principle III (Testing Standards). Unit tests for services/tools, integration tests for endpoints.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create the Deviation entity, enums, DTOs, and service interface — the type definitions everything else builds on.

- [x] T001 Create Deviation entity, DeviationType enum, DeviationStatus enum, and request/response DTOs in src/Ato.Copilot.Core/Models/Compliance/DeviationModels.cs
- [x] T002 Create IDeviationService interface with methods for CRUD, review, revoke, extend, list (filtered/paginated), summary, and boundary-scoped waiver queries in src/Ato.Copilot.Core/Interfaces/Compliance/IDeviationService.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented — DbContext, service implementation, migration, and DI registration.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Add DbSet&lt;Deviation&gt;, configure entity relationships (FKs to RegisteredSystem, ComplianceFinding, PoamItem, AuthorizationDecision, AuthorizationBoundaryDefinition), add indexes (SystemId, Status, FindingId, ExpirationDate), and add nullable DeviationId FK column to ComplianceFinding and PoamItem in src/Ato.Copilot.Core/Data/AtoCopilotContext.cs
- [x] T004 [P] Implement DeviationService with CRUD operations, status transitions (Pending→Approved/Denied, Approved→Expired/Revoked), finding/POA&M status cascade (approve→FalsePositive/RiskAccepted, expire/revoke→Open/Ongoing), duplicate-active-deviation check (409 Conflict), and DashboardActivity audit logging for all state transitions in src/Ato.Copilot.Core/Services/DeviationService.cs
- [x] T005 [P] Create EF Core migration with: (1) create Deviations table with all columns and indexes, (2) add DeviationId FK column to ComplianceFindings and PoamEntries, (3) migrate existing RiskAcceptance records into Deviation records using field mapping from research.md R1, (4) drop RiskAcceptances table in src/Ato.Copilot.Core/Migrations/
- [x] T006 Register IDeviationService → DeviationService in DI container and remove RiskAcceptance DbSet references in src/Ato.Copilot.Mcp/Program.cs
- [x] T037 [P] Write unit tests for DeviationService: lifecycle transitions (Pending→Approved/Denied, Approved→Expired/Revoked), finding/POA&M status cascade, duplicate-active check (409), CAT I ISSM recommendation flow, max review cycle validation (365d), and orphaned deviation handling in tests/Ato.Copilot.Tests.Unit/Services/DeviationServiceTests.cs

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 — Deviation Record Lifecycle (Priority: P1) 🎯 MVP

**Goal**: Expose the full deviation lifecycle via REST endpoints so deviations can be created, reviewed, revoked, and extended through the dashboard API.

**Independent Test**: POST a deviation, GET to confirm Pending status, PUT review to approve, verify finding/POA&M status cascade, PUT revoke to revert.

### Implementation for User Story 1

- [x] T007 [US1] Add deviation CRUD endpoints (GET /systems/{systemId}/deviations with filtering/pagination, GET /systems/{systemId}/deviations/summary, GET /deviations/{deviationId}, POST /systems/{systemId}/deviations) to src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T008 [US1] Add deviation workflow endpoints (PUT /deviations/{deviationId}/review with CAT I two-step ISSM recommendation flow, PUT /deviations/{deviationId}/revoke, PUT /deviations/{deviationId}/extend with MaxReviewCycleDays validation) with RBAC enforcement (ISSM for CAT II/III final + CAT I recommendation, AO for CAT I final) to src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T009 [US1] Update existing compliance_accept_risk MCP tool to create Deviation records with DeviationType=RiskAcceptance instead of RiskAcceptance records in src/Ato.Copilot.Agents/Compliance/Tools/
- [ ] T038 [P] [US1] Write integration tests for deviation CRUD endpoints (list, summary, detail, create) and workflow endpoints (review, revoke, extend) covering happy path, RBAC enforcement, CAT I two-step ISSM→AO approval, and error responses (409 duplicate, 400 not-pending) in tests/Ato.Copilot.Tests.Integration/Endpoints/DeviationEndpointsTests.cs
- [x] T039 [US1] Handle orphaned deviations: when a linked finding is deleted, transition the deviation to a terminal state and log a DashboardActivity audit record in src/Ato.Copilot.Core/Services/DeviationService.cs

**Checkpoint**: Deviation lifecycle is fully functional via REST API. The feature is usable via API calls.

---

## Phase 4: User Story 2 — Dashboard Deviations Page (Priority: P1)

**Goal**: Provide a dedicated dashboard page for managing deviations with summary cards, filterable table, detail drawer, and inline actions.

**Independent Test**: Navigate to /systems/:id/deviations, verify summary cards, filter by type/status, open detail drawer, approve a pending deviation from the drawer.

### Implementation for User Story 2

- [X] T010 [P] [US2] Create DeviationSummaryCards component with MetricCard pattern showing total, pending, expiring ≤ 30d, and CAT I deviation counts in src/Ato.Copilot.Dashboard/src/components/DeviationSummaryCards.tsx
- [X] T011 [P] [US2] Create DeviationTable component with tabs (All, False Positives, Risk Acceptances, Waivers), status/severity filters, text search, and paginated rows in src/Ato.Copilot.Dashboard/src/components/DeviationTable.tsx
- [X] T012 [US2] Create DeviationDetailDrawer with full justification, compensating controls, evidence list (hydrated from ScanImportRecord), approval history timeline, linked finding/POA&M cards, and Approve/Deny/Revoke/Extend action buttons in src/Ato.Copilot.Dashboard/src/components/DeviationDetailDrawer.tsx
- [X] T013 [US2] Create DeviationsPage composing DeviationSummaryCards, DeviationTable, and DeviationDetailDrawer with data fetching via deviation API endpoints in src/Ato.Copilot.Dashboard/src/pages/DeviationsPage.tsx
- [X] T014 [US2] Add /systems/:id/deviations route pointing to DeviationsPage in src/Ato.Copilot.Dashboard/src/App.tsx

**Checkpoint**: Deviations page is navigable and fully functional for listing, filtering, viewing details, and performing actions.

---

## Phase 5: User Story 3 — Boundary-Scoped Waivers (Priority: P2)

**Goal**: Enable waiver-type deviations scoped to specific authorization boundaries, with gap analysis exclusion and waived-coverage metrics.

**Independent Test**: Create a waiver for boundary "Sensor Network" on control AU-6, verify gap analysis excludes AU-6 for that boundary only, verify "Waived" badge appears, verify other boundaries still count AU-6.

### Implementation for User Story 3

- [X] T015 [US3] Add boundary-scoped waiver filtering logic and gap analysis exclusion queries (exclude waived controls from coverage calculation per boundary, compute separate waived-coverage metric) to DeviationService in src/Ato.Copilot.Core/Services/DeviationService.cs
- [X] T016 [US3] Update gap analysis endpoint to call waiver exclusion queries, return waived controls list with boundary scope, and include waived-coverage metric in response in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T017 [US3] Add "Waived" badge with dotted border to waived controls in coverage matrix and add "Show Waived Controls" toggle to gap analysis UI components in src/Ato.Copilot.Dashboard/src/
- [X] T040 [US3] Handle boundary deletion cascade: when an authorization boundary is deleted, reassign scoped waivers to the Primary boundary and flag for review, matching Feature 033 cascade behavior in src/Ato.Copilot.Core/Services/DeviationService.cs

**Checkpoint**: Boundary-scoped waivers exclude controls from gap analysis per boundary and are visually indicated.

---

## Phase 6: User Story 4 — Chat-Driven Deviation Management (Priority: P2)

**Goal**: Enable deviation management through natural language chat across dashboard, Teams, and VS Code via 5 MCP tools and a Teams Adaptive Card.

**Independent Test**: Issue a natural-language false-positive request in dashboard chat, verify deviation is created via compliance_request_deviation, approve via compliance_review_deviation, list via compliance_list_deviations.

### Implementation for User Story 4

- [X] T018 [US4] Implement 5 MCP deviation tools (compliance_request_deviation, compliance_review_deviation, compliance_list_deviations, compliance_revoke_deviation, compliance_extend_deviation) extending BaseTool per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/DeviationTools.cs
- [X] T019 [US4] Register all 5 deviation tools via RegisterTool() in src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs
- [X] T020 [P] [US4] Create deviationCard.ts Teams Adaptive Card rendering deviation details (control, severity, justification, evidence count) with Approve/Deny Action.Submit buttons in extensions/m365/src/cards/deviationCard.ts
- [X] T021 [US4] Export deviation card builder from extensions/m365/src/cards/index.ts
- [X] T041 [US4] Implement VS Code finding context menu: add "Request False Positive" action that opens a justification input and calls compliance_request_deviation MCP tool in extensions/vscode/src/
- [X] T042 [P] [US4] Write unit tests for 5 MCP deviation tools covering success paths, error cases (DEVIATION_NOT_FOUND, NOT_PENDING, DUPLICATE_DEVIATION, INVALID_INPUT), and CAT I ISSM recommendation recording in tests/Ato.Copilot.Tests.Unit/Tools/DeviationToolsTests.cs

**Checkpoint**: Deviations can be created, reviewed, listed, revoked, and extended via chat in all three surfaces.

---

## Phase 7: User Story 5 — Intelligent Suggestions Integration (Priority: P2)

**Goal**: Surface deviation-related and outstanding-info suggestions as actionable cards in the chat panel's Intelligent Suggestions engine.

> **Note**: US5 (frontend suggestions) and US6 (backend TodoService) detect the same outstanding-info conditions. Extract shared detection queries in the backend and expose via API so both surfaces use identical logic, preventing drift.

**Independent Test**: Set up deviations with upcoming expirations, missing evidence, and incomplete system records. Verify suggestion cards appear with correct labels, priorities, and prompts.

### Implementation for User Story 5

- [X] T022 [US5] Add deviation suggestions to getIntelligentSuggestions: expiring deviations (priority 85), pending reviews for ISSM/AO (priority 90), and deviations missing evidence (priority 70) in src/Ato.Copilot.Dashboard/src/components/chat/phasePageSuggestions.ts
- [X] T023 [US5] Add outstanding-info suggestions to getIntelligentSuggestions: missing document due dates, POA&M missing completion dates, SSP sections in Draft/NeedsRevision, authorization decision missing expiration, CAT I findings without remediation or deviation (priority 75-95) in src/Ato.Copilot.Dashboard/src/components/chat/phasePageSuggestions.ts

**Checkpoint**: Suggestion cards appear in the chat panel for deviation and outstanding-info conditions.

---

## Phase 8: User Story 6 — Todo Panel Deviation & Outstanding Items (Priority: P2)

**Goal**: Populate the Todo panel with deviation-related tasks and outstanding-info items so users see "what to do next" without checking each page individually.

**Independent Test**: Set up pending deviations, expiring deviations, and missing data fields. Verify Todo items appear with correct labels, categories, links, and prompts.

### Implementation for User Story 6

- [x] T024 [US6] Add `deviation` category to TodoService generating items for pending reviews ("Review N pending deviation requests"), expiring deviations ("Renew N expiring deviations"), and CAT I AO approvals ("N CAT I deviations require your approval") with links to Deviations page and contextual chat prompts in src/Ato.Copilot.Core/Services/TodoService.cs
- [x] T025 [US6] Add `outstanding-info` category to TodoService generating items for missing document due dates, POA&M missing scheduled completion dates, deviations without evidence, SSP sections in Draft/NeedsRevision, and authorization decisions without expiration dates with links to relevant pages in src/Ato.Copilot.Core/Services/TodoService.cs

**Checkpoint**: Todo panel shows deviation and outstanding-info items with actionable links and chat prompts.

---

## Phase 9: User Story 7 — Cross-Page Deviation Indicators (Priority: P3)

**Goal**: Surface deviation context on other dashboard pages so users discover deviation information without navigating to the Deviations page.

**Independent Test**: Create approved deviations of all three types, verify "Active Deviations" metric on System Detail, "View Deviation" link on Remediation POA&M drawer, deviation badge on Assessments findings.

### Implementation for User Story 7

- [x] T026 [P] [US7] Add "Active Deviations" MetricCard (count with link to Deviations page) to System Detail key metrics row in src/Ato.Copilot.Dashboard/src/components/SystemDetail.tsx
- [x] T027 [P] [US7] Add "View Deviation" link to Remediation page POA&M detail drawer for risk-accepted items, linking through to the deviation record in src/Ato.Copilot.Dashboard/src/
- [x] T028 [US7] Update system detail endpoint to include activeDeviations count in response in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T043 [P] [US7] Add waiver justification indicators to Documents page: show waiver badge on SSP sections for waived controls with justification text in src/Ato.Copilot.Dashboard/src/
- [x] T044 [P] [US7] Add deviation badges (FalsePositive, RiskAccepted) to findings on the Assessments page with link to deviation record in src/Ato.Copilot.Dashboard/src/

**Checkpoint**: Deviation indicators are visible on System Detail, Remediation, Documents, and Assessments pages.

---

## Phase 10: User Story 8 — Notifications & Alerts (Priority: P3)

**Goal**: Send proactive notifications for deviation lifecycle events and include deviations in the daily digest.

**Independent Test**: Create a deviation, verify reviewer notification fires. Approve it, verify requestor notification. Set expiration to trigger 30d/7d/expired alerts. Verify daily digest includes deviations section.

### Implementation for User Story 8

- [x] T029 [US8] Create DeviationExpirationService as IHostedService (daily 06:00 UTC): query expired approved deviations, set status=Expired, revert finding to Open and POA&M to Ongoing, log DashboardActivity audit records, fire expiration notifications via INotificationBroadcaster; also fire 30d and 7d expiration warning notifications in src/Ato.Copilot.Agents/Compliance/Services/DeviationExpirationService.cs
- [x] T030 [US8] Register DeviationExpirationService as hosted service in src/Ato.Copilot.Mcp/Program.cs
- [x] T031 [US8] Add notification broadcasts for deviation lifecycle events (creation→reviewer, approval/denial→requestor) to DeviationService methods in src/Ato.Copilot.Core/Services/DeviationService.cs
- [x] T032 [US8] Extend daily digest to include a deviations section summarizing pending reviews and upcoming expirations in existing DigestSchedulerHostedService

**Checkpoint**: All deviation lifecycle events trigger appropriate notifications and appear in the daily digest.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Export enrichment, documentation, and final validation across all user stories.

- [x] T033 [P] Enrich eMASS POA&M export with 3 new columns (Deviation Justification, Deviation Type, Deviation Expiration) via LEFT JOIN to Deviation for risk-accepted items in src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs
- [x] T034 [P] Add deviation props on control-implementation statements, deviation resources in back-matter, and waiver justification text in SSP narrative content for boundary-scoped waivers in OSCAL SSP export in src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs
- [x] T035 [P] Create deviation management user guide in docs/guides/deviation-management.md and update docs/api/mcp-server.md with 5 new tool references
- [x] T036 Run quickstart.md verification checklist to validate end-to-end feature completeness

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (Phase 1) completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational (Phase 2) — first story to implement (MVP)
- **US2 (Phase 4)**: Depends on US1 (Phase 3) — needs REST endpoints to fetch data
- **US3 (Phase 5)**: Depends on Foundational (Phase 2) — can start in parallel with US1 if backend only
- **US4 (Phase 6)**: Depends on Foundational (Phase 2) — MCP tools use DeviationService directly
- **US5 (Phase 7)**: Depends on US1 (Phase 3) — needs deviation API data in chat context
- **US6 (Phase 8)**: Depends on Foundational (Phase 2) — TodoService uses DeviationService directly
- **US7 (Phase 9)**: Depends on US1 (Phase 3) — needs deviation count endpoint
- **US8 (Phase 10)**: Depends on Foundational (Phase 2) — background service uses DeviationService
- **Polish (Phase 11)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Start after Phase 2 — no dependencies on other stories
- **US2 (P1)**: Depends on US1 endpoints being available
- **US3 (P2)**: Can start after Phase 2 — independent of US1/US2
- **US4 (P2)**: Can start after Phase 2 — independent of US1/US2
- **US5 (P2)**: Needs deviation data in chat context — depends on US1 API endpoints
- **US6 (P2)**: Can start after Phase 2 — independent of US1/US2
- **US7 (P3)**: Needs deviation count endpoint from US1
- **US8 (P3)**: Can start after Phase 2 — DeviationExpirationService is independent

### Within Each User Story

- Backend before frontend (service before endpoint before UI)
- Models before services, services before endpoints
- Core implementation before integration

### Parallel Opportunities

After Phase 2 completes, the following can proceed in parallel:
- **Track A (Backend)**: US1 → US3, US7 (endpoint work)
- **Track B (MCP Tools)**: US4 (DeviationTools + ComplianceAgent registration)
- **Track C (TodoService)**: US6 (backend-only, same codebase but different files)
- **Track D (Background Service)**: US8 (DeviationExpirationService)

After US1 completes:
- **Track E (Frontend)**: US2 → US5 → US7 UI tasks
- **Track F (Exports)**: Polish T033, T034

---

## Parallel Example: After Phase 2 Completion

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ Phase 2 Complete (T003-T006, T037)                                    │
├─────────────────┬──────────────┬──────────────┬────────────────────────┤
│ Track A: US1    │ Track B: US4 │ Track C: US6 │ Track D: US8           │
│ T007 endpoints  │ T018 tools   │ T024 todos   │ T029 expiration svc    │
│ T008 workflow   │ T019 register│ T025 todos   │ T030 register          │
│ T009 accept_risk│ T020 card    │              │ T031 notifications     │
│ T038 integ tests│ T021 export  │              │ T032 digest            │
│ T039 orphan hdl │ T041 vscode  │              │                        │
│                 │ T042 tool tst│              │                        │
├─────────────────┴──────────────┴──────────────┴────────────────────────┤
│ US1 Complete (T007-T009, T038-T039)                                    │
├──────────────────┬──────────────┬──────────────────────────────────────┤
│ Track E: US2     │ Track F: US3 │ Track G: US5, US7                    │
│ T010 cards       │ T015 service │ T022 suggestions                     │
│ T011 table       │ T016 endpoint│ T023 outstanding-info                │
│ T012 drawer      │ T017 UI      │ T026 system detail card              │
│ T013 page        │ T040 cascade │ T027 remediation link                │
│ T014 route       │              │ T028 endpoint                        │
│                  │              │ T043 documents                       │
│                  │              │ T044 assessments                     │
└──────────────────┴──────────────┴──────────────────────────────────────┘
```

---

## Implementation Strategy

### MVP Scope (Recommended First Delivery)

Phases 1–4 (Setup + Foundational + US1 + US2) deliver a fully usable deviation management feature:
- Create, review, revoke, and extend deviations via API and dashboard page
- Unit and integration test coverage for core service and endpoints
- Summary cards, filterable table, detail drawer with inline actions
- Finding/POA&M status cascade on approval/expiration/revocation
- RiskAcceptance migration complete

### Incremental Delivery After MVP

1. **US4 (Chat-Driven)**: MCP tools enable chat-based deviation management across all three surfaces
2. **US6 (Todo Panel)**: Deviation and outstanding-info items appear in "what to do next" panel
3. **US3 (Boundary Waivers)**: Boundary-scoped waivers with gap analysis exclusion
4. **US5 (Suggestions)**: Proactive suggestion cards for deviations and outstanding info
5. **US7 + US8 (Cross-Page + Notifications)**: Indicators on other pages and lifecycle notifications
6. **Polish**: Export enrichment and documentation
