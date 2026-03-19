# Tasks: POA&M Management (Feature 039)

**Input**: Design documents from `/specs/039-poam-management/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-endpoints.md, contracts/mcp-tools.md, quickstart.md

**Tests**: Test tasks are included per Constitution III (Testing Standards). Unit tests for services and tools, integration tests for API endpoints, and frontend component tests are distributed across phases.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. 11 user stories across 3 priority tiers (P1×6, P2×4, P3×1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US4a)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New entity models, enums, database schema, and TypeScript types that all stories depend on

- [X] T001 Create PoamComponentLink and PoamHistoryEntry entity models in src/Ato.Copilot.Core/Models/Poam/PoamModels.cs
- [X] T002 [P] Create TicketingIntegration and PoamTicketSync entity models in src/Ato.Copilot.Core/Models/Poam/TicketingModels.cs
- [X] T003 [P] Create enums PoamHistoryEventType (17 values), CascadeOrigin, TicketingProvider, TicketSyncStatus in src/Ato.Copilot.Core/Models/Poam/PoamEnums.cs
- [X] T004 Extend PoamItem in src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs to inherit ConcurrentEntity and add CreatedBy, ModifiedBy, ExternalTicketRef, ComponentLinks and History collection nav properties
- [X] T005 [P] Extend RemediationTask in src/Ato.Copilot.Core/Models/Kanban/KanbanModels.cs to add PoamItem navigation property for bidirectional sync support
- [X] T006 Update AtoCopilotContext in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs with new DbSets (PoamComponentLinks, PoamHistoryEntries, TicketingIntegrations, PoamTicketSyncs), entity configurations, relationships, and indexes per data-model.md
- [X] T007 Create EF Core migration for POA&M schema changes (new tables, extended columns, indexes, FK constraints with SetNull delete behavior)
- [X] T008 [P] Create POA&M TypeScript interfaces in src/Ato.Copilot.Dashboard/src/types/poam.ts matching all DTOs from contracts/api-endpoints.md (PoamListItem, PoamDetail, CreatePoamRequest, UpdatePoamStatusRequest, PoamMetrics, PoamTrendResponse, etc.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core service layer, API routes, and frontend client infrastructure that MUST be complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T009 Create PoamService.cs in src/Ato.Copilot.Core/Services/PoamService.cs with core CRUD methods (CreateAsync, GetByIdAsync, ListAsync with server-side pagination per R-004, UpdateAsync, DeleteAsync) and register in DI
- [X] T010 Add core POA&M REST endpoints to src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs: GET list (system-scoped + cross-system), GET detail, POST create, PUT update — following existing endpoint patterns with PaginatedResponse<T> and ErrorResponse
- [X] T011 [P] Create POA&M API client in src/Ato.Copilot.Dashboard/src/api/poam.ts with Axios methods for list, getById, create, update, delete, and metrics
- [X] T012 [P] Create usePoam.ts hooks in src/Ato.Copilot.Dashboard/src/hooks/usePoam.ts for data fetching (usePoamList, usePoamDetail, usePoamMetrics) and mutations (useCreatePoam, useUpdatePoam)
- [X] T013 Implement RBAC authorization in PoamService.cs: ISSO/ISSM/AO/ComplianceOfficer can create/update/delete; Engineer role has read-only access with cascade passthrough per FR-019
- [X] T014 [P] Unit tests for PoamService CRUD operations in tests/Ato.Copilot.Tests.Unit/Services/PoamServiceTests.cs — positive and negative cases for Create, GetById, List (pagination boundaries), Update, Delete, and RBAC enforcement per Constitution III

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — POA&M Dashboard Overview (Priority: P1) 🎯 MVP

**Goal**: Dedicated POA&M Management page with summary cards, severity heatbar, paginated table, detail drawer, creation form, and navigation integration

**Independent Test**: Navigate to /systems/:id/poam, verify summary cards and severity heatbar render, table loads with pagination, detail drawer opens on row click with concurrency conflict handling, and new POA&M can be created via the form

### Implementation for User Story 1

- [X] T015 [P] [US1] Add POA&M routes to src/Ato.Copilot.Dashboard/src/App.tsx: /systems/:id/poam (system-scoped), /poam (org-level), and /systems/:id/components (wire existing ComponentInventory.tsx) per R-010
- [X] T016 [P] [US1] Add "Components" nav item (between Boundaries and Capability Coverage) and "POA&M" nav item (below Remediation) to src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx per FR-001/FR-001a
- [X] T017 [US1] Create PoamManagement.tsx page in src/Ato.Copilot.Dashboard/src/pages/PoamManagement.tsx with page header ("POA&M Management"), descriptive subtext, and "Add POA&M" action button (hidden for read-only roles per FR-019)
- [X] T018 [P] [US1] Create PoamSummaryCards.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamSummaryCards.tsx displaying total open, overdue, CAT I, expiring within 30 days, and avg days to close per FR-002
- [X] T019 [P] [US1] Create PoamSeverityHeatbar.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamSeverityHeatbar.tsx visualizing CAT I/II/III distribution as a color-coded horizontal bar per US1 narrative
- [X] T020 [US1] Create PoamTable.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamTable.tsx with server-side paginated table (default 25, selector 25/50/100), sortable columns (control ID, weakness, severity, status, components, POC, due date, days remaining, milestone progress, deviation type, external ticket ref), and filter bar (system, status, CAT severity, overdue, component, source, free-text search) per FR-003/FR-004
- [X] T021 [US1] Create PoamDetailDrawer.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamDetailDrawer.tsx with full metadata display, milestones section, linked entities panel (finding, remediation task, deviation, components, external ticket), optimistic concurrency handling via rowVersion, and concurrency conflict dialog (shows server version vs. local changes with "Reload" and "Force Save" options) per FR-012/FR-014
- [X] T022 [US1] Create PoamCreateForm.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamCreateForm.tsx with fields: weakness, source, control ID, CAT severity, POC, POC email, scheduled completion date, resources required, milestones, comments, and findingId
- [X] T023 [US1] Add metrics endpoints (GET /api/dashboard/systems/{systemId}/poam/metrics and GET /api/dashboard/poam/metrics) to DashboardEndpoints.cs returning PoamMetrics DTO per FR-002
- [X] T024 [P] [US1] Implement GetPoamTool.cs (compliance_get_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/GetPoamTool.cs extending BaseTool with poam_id and include_history parameters, register in ComplianceMcpTools per FR-012
- [X] T025 [P] [US1] Update compliance_create_poam tool to add component_ids (optional string[]) and remediation_task_id (optional string) parameters per contracts/mcp-tools.md
- [X] T026 [P] [US1] Update compliance_list_poam tool to add filters (component_id, overdue_only, deviation_type, has_remediation_task, source) and include_metrics flag per contracts/mcp-tools.md
- [X] T027 [US1] Integrate PoamSummaryCards, PoamSeverityHeatbar, PoamTable, PoamCreateForm, and PoamDetailDrawer into PoamManagement.tsx with data wiring via usePoam hooks

**Checkpoint**: POA&M Management page fully functional with CRUD, pagination, filtering, and MCP tool access — MVP deliverable

---

## Phase 4: User Story 2 — Component-Linked POA&M Creation (Priority: P1)

**Goal**: POA&M items can be linked to one or more system components from the HW/SW inventory for asset-level risk tracking

**Independent Test**: Create a POA&M and link it to 2 components via the ComponentPicker. View POA&M detail and verify components display. View ComponentInventory and verify risk badges. Use compliance_poam_by_component to list POA&Ms for a component with risk summary.

### Implementation for User Story 2

- [X] T028 [P] [US2] Create ComponentPicker.tsx in src/Ato.Copilot.Dashboard/src/components/poam/ComponentPicker.tsx — searchable multi-select component selector that fetches from existing HW/SW inventory API, validates same-system membership
- [X] T029 [US2] Add component link/unlink endpoints (POST and DELETE /api/dashboard/poam/{poamId}/components) to DashboardEndpoints.cs per api-endpoints.md
- [X] T030 [US2] Implement component linkage methods in PoamService.cs: LinkComponentsAsync, UnlinkComponentsAsync, GetPoamsByComponentAsync with aggregate risk summary (highest CAT severity, open count, overdue count) per R-007
- [X] T031 [P] [US2] Create LinkPoamComponentTool.cs (compliance_link_poam_component) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/LinkPoamComponentTool.cs, register in ComplianceMcpTools
- [X] T032 [P] [US2] Create UnlinkPoamComponentTool.cs (compliance_unlink_poam_component) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/UnlinkPoamComponentTool.cs, register in ComplianceMcpTools
- [X] T033 [P] [US2] Create PoamByComponentTool.cs (compliance_poam_by_component) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/PoamByComponentTool.cs, register in ComplianceMcpTools
- [X] T034 [US2] Integrate ComponentPicker into PoamCreateForm.tsx (optional component selection on creation) and add component section to PoamDetailDrawer.tsx (linked components list with link/unlink actions)
- [X] T035 [US2] Add aggregate POA&M risk badges to src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx — each component displays a risk badge based on highest CAT severity of linked open POA&Ms, querying via GetPoamsByComponentAsync per US2 AC4

**Checkpoint**: POA&M items can be created with component linkage and managed from the detail drawer; Component Inventory shows risk badges

---

## Phase 5: User Story 3 — Auto-Generate POA&Ms from Scans (Priority: P1)

**Goal**: POA&M items are auto-generated from vulnerability scan findings and assessment results with duplicate detection

**Independent Test**: Import a scan with 10+ findings, verify post-import prompt appears offering POA&M creation, verify POA&M items are auto-created with correct metadata. Re-import the same scan and verify zero duplicates. Run assessment board creation and verify each task has a linked POA&M.

### Implementation for User Story 3

- [X] T036 [US3] Implement BulkCreateFromFindingsAsync in PoamService.cs with duplicate detection (match on findingRef + controlId + componentId per FR-006), component auto-linkage when finding maps to inventory item, and batch progress tracking
- [X] T037 [US3] Extend CreateBoardFromAssessmentAsync in src/Ato.Copilot.Core/Services/KanbanService.cs to auto-create PoamItem alongside each RemediationTask, set bidirectional FKs, reuse existing active POA&M when duplicate detected per R-008/FR-006a
- [X] T038 [US3] Add bulk-create endpoint (POST /api/dashboard/systems/{systemId}/poam/bulk-create) to DashboardEndpoints.cs returning BulkCreateResponse with created/skipped/error counts per api-endpoints.md
- [X] T039 [P] [US3] Create BulkCreatePoamFromFindingsTool.cs (compliance_bulk_create_poam_from_findings) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/BulkCreatePoamFromFindingsTool.cs, register in ComplianceMcpTools
- [X] T040 [P] [US3] Update compliance_import_nessus tool to link components when finding maps to known inventory item and return poam_items_created, poam_items_deduplicated, component_links_created counts per contracts/mcp-tools.md
- [X] T041 [US3] Create PostImportPoamPrompt.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PostImportPoamPrompt.tsx — dialog shown after scan imports listing findings without POA&M items, with individual/bulk selection and "Create POA&M Items" action. Grays out findings with existing active POA&Ms per US3 AC3/AC5

**Checkpoint**: Scan imports and assessment boards auto-generate POA&M items with zero duplicate entries (SC-002, SC-002a)

---

## Phase 6: User Story 4 — POA&M Lifecycle Management (Priority: P1)

**Goal**: POA&M items track full lifecycle (Ongoing → Delayed/Completed/Risk Accepted) with enforced transition rules and complete audit trail

**Independent Test**: Transition a POA&M through Ongoing → Delayed (with explanation) → Ongoing (with revised date) → Completed (with finding validation), verify each transition is recorded in the audit timeline with actor, timestamp, and details

### Implementation for User Story 4

- [X] T042 [US4] Implement lifecycle transition logic in PoamService.cs: UpdateStatusAsync with rules — Delayed requires delay_reason + revised_date; Resume (Delayed → Ongoing) requires revised completion date; Completed validates linked finding status; Risk Accepted requires linked deviation_id per FR-007
- [X] T043 [US4] Implement audit trail recording in PoamService.cs: AddHistoryEntryAsync for all status changes, field edits, milestone updates, and comment additions using PoamHistoryEventType enum per FR-008
- [X] T044 [US4] Add status update endpoint (PUT /api/dashboard/poam/{poamId}/status) and bulk status endpoint (POST /api/dashboard/poam/bulk-status) to DashboardEndpoints.cs with lifecycle validation and rowVersion concurrency per api-endpoints.md
- [X] T045 [US4] Create PoamLifecycleActions.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamLifecycleActions.tsx with status transition buttons and dialogs: "Mark Delayed" (reason + revised date form), "Resume" (revised completion date), "Mark Completed" (finding validation check + warning), "Risk Accepted" (deviation record picker) per FR-007
- [X] T046 [US4] Add history timeline tab to PoamDetailDrawer.tsx showing reverse-chronological audit entries with actor, timestamp, event type, old/new values, and cascade origin details per FR-008
- [X] T047 [P] [US4] Create UpdatePoamTool.cs (compliance_update_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/UpdatePoamTool.cs with lifecycle enforcement and auto-cascade per FR-008c, register in ComplianceMcpTools
- [X] T048 [P] [US4] Create ClosePoamTool.cs (compliance_close_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/ClosePoamTool.cs with finding validation and cascade_to_task, register in ComplianceMcpTools
- [X] T049 [P] [US4] Create UpdatePoamMilestoneTool.cs (compliance_update_poam_milestone) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/UpdatePoamMilestoneTool.cs, register in ComplianceMcpTools
- [X] T050 [P] [US4] Create BulkUpdatePoamTool.cs (compliance_bulk_update_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/BulkUpdatePoamTool.cs with per-item results, register in ComplianceMcpTools
- [X] T051 [P] [US4] Unit tests for lifecycle transitions and audit trail in tests/Ato.Copilot.Tests.Unit/Services/PoamServiceTests.cs — cover all status transitions (valid + invalid), required field enforcement (delay_reason, deviation_id), finding validation on close, and PoamHistoryEntry creation per Constitution III

**Checkpoint**: Full lifecycle management with audit trail — every status change has recorded actor, timestamp, and context (SC-007)

---

## Phase 7: User Story 4a — Remediation-POA&M Bidirectional Sync (Priority: P1)

**Goal**: POA&M items and remediation tasks are bidirectionally linked with cascade propagation for status, severity, and due date changes

**Independent Test**: Create a remediation task from a POA&M, verify fields pre-populated. Move task to "Done", verify POA&M prompts for completion. Change POA&M due date, verify task due date updates. Unlink and verify both FKs cleared with history entries.

### Implementation for User Story 4a

- [X] T052 [US4a] Create PoamSyncService.cs in src/Ato.Copilot.Core/Services/PoamSyncService.cs with bidirectional cascade logic: CascadeStatusChangeAsync, CascadeMetadataChangeAsync, CreateTaskFromPoamAsync, LinkAsync, UnlinkAsync — using CascadeOrigin tracking to prevent infinite loops per R-001/FR-008c/FR-008d
- [X] T053 [US4a] Add remediation-task endpoints to DashboardEndpoints.cs: POST /poam/{poamId}/task (create task from POA&M), POST /poam/{poamId}/link-task, DELETE /poam/{poamId}/unlink-task per api-endpoints.md
- [X] T054 [US4a] Create CascadeConfirmDialog.tsx in src/Ato.Copilot.Dashboard/src/components/poam/CascadeConfirmDialog.tsx for UI cascade prompts: "Linked remediation task completed — mark POA&M as Completed?", "Update linked task due date?", etc. per FR-008c
- [X] T055 [US4a] Create SyncIndicator.tsx in src/Ato.Copilot.Dashboard/src/components/poam/SyncIndicator.tsx showing link status (synced/conflict), linked entity name, last sync timestamp, and clickable navigation to linked entity per FR-008e
- [X] T056 [US4a] Integrate CascadeConfirmDialog and SyncIndicator into PoamDetailDrawer.tsx, add "Create Remediation Task", "Link to Task", and "Unlink" action buttons per FR-008a/FR-008b
- [X] T057 [P] [US4a] Create LinkPoamTaskTool.cs (compliance_link_poam_task) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/LinkPoamTaskTool.cs with bidirectional FK setting and reject-if-already-linked validation, register in ComplianceMcpTools
- [X] T058 [P] [US4a] Create UnlinkPoamTaskTool.cs (compliance_unlink_poam_task) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/UnlinkPoamTaskTool.cs with bidirectional FK clearing and history entries on both entities, register in ComplianceMcpTools
- [X] T059 [P] [US4a] Create CreateTaskFromPoamTool.cs (compliance_create_task_from_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/CreateTaskFromPoamTool.cs with field mapping (weakness→title, catSeverity→taskSeverity, poc→assignee), register in ComplianceMcpTools
- [X] T060 [US4a] Wire cascade prompts into lifecycle actions — status/metadata changes on linked entities trigger CascadeConfirmDialog in dashboard UI; API/MCP calls auto-apply cascades with full audit trail per FR-008c
- [X] T061 [P] [US4a] Unit tests for PoamSyncService in tests/Ato.Copilot.Tests.Unit/Services/PoamSyncServiceTests.cs — cover bidirectional cascade (task→POA&M, POA&M→task), CascadeOrigin circular prevention, link/unlink FK management, metadata propagation (severity, due date), and conflict detection per Constitution III

**Checkpoint**: Bidirectional sync operational — linked entities stay synchronized with cascade tracking and circular prevention

---

## Phase 8: User Story 4b — Remediation Page Refocus (Priority: P1)

**Goal**: Remediation page manages only remediation tasks — POA&M elements moved to POA&M Management page, task table gains "Linked POA&M" column, kanban cards support click-to-open-detail and show POA&M link badges

**Independent Test**: Navigate to Remediation page, verify POA&M summary cards and aging chart are removed. Verify the task table shows a "Linked POA&M" column with clickable badges. Verify kanban cards show POA&M-linked badge. Click a kanban card and verify detail drawer opens. Open an unlinked task and verify "Link to POA&M" action is available.

### Implementation for User Story 4b

- [X] T062 [US4b] Remove POA&M-specific elements from src/Ato.Copilot.Dashboard/src/pages/Remediation.tsx: POA&M summary cards (Open POA&Ms, Overdue, CAT I Open, Avg Days to Close), severity heatbar, POA&M aging chart, and milestone/deviation table columns per FR-016
- [X] T063 [US4b] Add "Linked POA&M" column to task table in Remediation.tsx displaying linked POA&M status badge (color-coded: Ongoing/Delayed/Completed/RiskAccepted) — clicking badge navigates to /systems/:id/poam?detail={poamId}; unlinked tasks show "—" per FR-017
- [X] T064 [US4b] Add click-to-open-detail handler on kanban task cards in Remediation.tsx — distinguish click vs. drag via pointerdown+pointermove threshold; clicking opens task detail drawer with full metadata per FR-018/R-009
- [X] T065 [US4b] Add SyncIndicator and "View POA&M" navigation link to task detail drawer in Remediation.tsx for tasks with linked POA&M items per FR-008e
- [X] T066 [US4b] Add "POA&M Linked" badge/icon to kanban card faces in Remediation.tsx for tasks with a non-null poamItemId — small status-colored indicator matching the table badge style per US3 AC7
- [X] T067 [US4b] Add "Link to POA&M" action button to task detail drawer for unlinked tasks — opens a searchable POA&M picker (open items for the same system) and calls POST /poam/{poamId}/link-task to establish bidirectional link per US4a AC2/FR-008b

**Checkpoint**: Remediation page shows only task-management UI with one-click POA&M navigation (SC-010)

---

## Phase 9: User Story 5 — Trend Reporting and Analytics (Priority: P2)

**Goal**: Historical trend dashboards with charts for open count, time-to-close, aging breakdown, and closure rate with system/severity/date-range filters and PDF export

**Independent Test**: Navigate to the trend dashboard section, verify charts render with historical data, apply a 90-day date range filter, verify charts update, and export a PDF trend report

### Implementation for User Story 5

- [X] T068 [US5] Implement trend calculation logic in PoamService.cs: GetTrendDataAsync (open over time, closure rate per period, aging breakdown by severity, time-to-close distribution) and GetMetricsAsync with date range filtering per FR-009
- [X] T069 [US5] Add trend endpoint (GET /api/dashboard/systems/{systemId}/poam/trend) to DashboardEndpoints.cs with period (daily/weekly/monthly) and date range query params returning PoamTrendResponse DTO per api-endpoints.md
- [X] T070 [US5] Create PoamTrendCharts.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamTrendCharts.tsx with Recharts: open count line chart, time-to-close histogram, severity aging stacked bar, monthly closure rate bar chart — with tooltips on hover per FR-009
- [X] T071 [US5] Integrate PoamTrendCharts into PoamManagement.tsx with system, severity, and date-range filter controls per US5 acceptance scenarios
- [X] T072 [US5] Implement trend report PDF export via QuestPDF in PoamService.cs: ExportTrendReportPdfAsync rendering visible charts as summary tables/graphics, applied filters, and summary statistics. Add GET /api/dashboard/systems/{systemId}/poam/trend/export endpoint returning PDF file download per US5 AC3
- [X] T073 [P] [US5] Create PoamMetricsTool.cs (compliance_poam_metrics) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/PoamMetricsTool.cs with system_id, date_range params, register in ComplianceMcpTools
- [X] T074 [P] [US5] Create PoamTrendTool.cs (compliance_poam_trend) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/PoamTrendTool.cs with system_id, period, date_range params, register in ComplianceMcpTools

**Checkpoint**: Trend dashboard with PDF export replaces manually compiled spreadsheets for authorization decision packages (SC-005)

---

## Phase 10: User Story 6 — External Ticketing Integration (Priority: P2)

**Goal**: Jira and ServiceNow integration with field mapping, webhook-based bidirectional sync, and Key Vault credential storage

**Independent Test**: Configure a Jira integration, sync a POA&M item, update the Jira issue status, trigger a sync, and verify the POA&M status reflects the change with sync indicator

### Implementation for User Story 6

- [X] T075 [US6] Create TicketingService.cs in src/Ato.Copilot.Core/Services/TicketingService.cs with ITicketingProvider interface, Key Vault credential retrieval via Azure.Security.KeyVault.Secrets SecretClient, sync orchestration (push/pull/bidirectional), and conflict detection per R-006/FR-010
- [X] T076 [P] [US6] Implement JiraProvider in src/Ato.Copilot.Core/Services/Ticketing/JiraProvider.cs for ITicketingProvider with issue create, status update, and webhook handling
- [X] T077 [P] [US6] Implement ServiceNowProvider in src/Ato.Copilot.Core/Services/Ticketing/ServiceNowProvider.cs for ITicketingProvider with incident create, status update, and webhook handling
- [X] T078 [US6] Add ticketing endpoints to DashboardEndpoints.cs: GET/POST /systems/{systemId}/ticketing (config), POST /poam/{poamId}/sync-ticket (single sync), POST /systems/{systemId}/poam/bulk-sync (batch) per api-endpoints.md
- [X] T079 [US6] Create TicketingConfig.tsx in src/Ato.Copilot.Dashboard/src/components/poam/TicketingConfig.tsx with configuration form (provider, URL, project/table, credentials, field mapping, sync toggle), sync status panel, and error display per FR-010
- [X] T080 [P] [US6] Create ConfigureTicketingTool.cs (compliance_configure_ticketing) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/ConfigureTicketingTool.cs with connectivity validation on save, register in ComplianceMcpTools
- [X] T081 [P] [US6] Create SyncPoamTicketTool.cs (compliance_sync_poam_ticket) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/SyncPoamTicketTool.cs with push/pull/bidirectional sync, register in ComplianceMcpTools
- [X] T082 [P] [US6] Create BulkSyncTicketsTool.cs (compliance_bulk_sync_tickets) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/BulkSyncTicketsTool.cs with per-item results, register in ComplianceMcpTools
- [X] T083 [US6] Integrate ticketing sync indicators and "Sync to Jira/ServiceNow" action into PoamDetailDrawer.tsx for configured systems, showing sync status, last sync time, and error details per FR-010
- [X] T084 [P] [US6] Unit tests for TicketingService in tests/Ato.Copilot.Tests.Unit/Services/TicketingServiceTests.cs — cover Key Vault credential retrieval, ITicketingProvider dispatch, sync orchestration (push/pull/bidirectional), conflict detection, and error handling per Constitution III

**Checkpoint**: Ticketing integration with ≤15-minute sync latency and <1% failure rate (SC-006)

---

## Phase 11: User Story 7 — POA&M Export and Compliance Reporting (Priority: P2)

**Goal**: Export POA&M data in eMASS Excel (24-column template), OSCAL JSON, and CSV formats with filter support

**Independent Test**: Filter POA&M items to CAT I + Ongoing, click "Export eMASS", verify downloaded Excel file contains only filtered items with all 24 eMASS template columns populated

### Implementation for User Story 7

- [X] T085 [US7] Implement export methods in PoamService.cs: ExportEmassExcelAsync (24-column template with deviation columns), ExportOscalJsonAsync (NIST OSCAL POA&M schema), ExportCsvAsync — all respecting active filters or include_all flag per FR-011
- [X] T086 [US7] Add export endpoint (GET /api/dashboard/systems/{systemId}/poam/export) to DashboardEndpoints.cs with format, status, severity, and includeAll query params returning file download per api-endpoints.md
- [X] T087 [US7] Create PoamExportDialog.tsx in src/Ato.Copilot.Dashboard/src/components/poam/PoamExportDialog.tsx with format selection (eMASS Excel, OSCAL JSON, CSV), filtered/all toggle, and download trigger per FR-011
- [X] T088 [US7] Integrate PoamExportDialog into PoamManagement.tsx toolbar as "Export" button per US7 acceptance scenarios
- [X] T089 [US7] Create ExportPoamTool.cs (compliance_export_poam) in src/Ato.Copilot.Agents/Compliance/Tools/Poam/ExportPoamTool.cs with format, filter params, register in ComplianceMcpTools

**Checkpoint**: Export workflow completes end-to-end without leaving the POA&M Management page (SC-008)

---

## Phase 12: User Story 8 — Chat-Driven POA&M Operations (Priority: P3)

**Goal**: Full POA&M lifecycle accessible via natural language across dashboard chat, Teams, and VS Code surfaces

**Independent Test**: Ask the dashboard chat "Show me all overdue POA&Ms" and verify a formatted table is returned matching the POA&M Management page data

### Implementation for User Story 8

- [X] T090 [US8] Update Compliance agent system prompts in src/Ato.Copilot.Agents/Compliance/ prompt files to include POA&M tool awareness, example invocations, and contextual guidance for POA&M operations
- [X] T091 [US8] Verify all 18 new POA&M tools are registered in ComplianceMcpTools and callable from MCP protocol — fix any registration gaps from prior phases
- [X] T092 [US8] Validate chat-driven POA&M operations end-to-end: list (formatted table), create (parameter parsing), update (lifecycle enforcement), trend (summary), and export (file delivery) across dashboard chat surface per SC-009

**Checkpoint**: All 21 POA&M tools (18 new + 3 updated) callable via natural language from all 3 surfaces (SC-009)

---

## Phase 13: User Story 9 — Documentation Updates (Priority: P2)

**Goal**: All 7 documentation areas updated so users can discover, learn, and reference POA&M workflows

**Independent Test**: For each documentation area, verify the page renders correctly in MkDocs, contains accurate content matching the implemented feature, and is reachable from MkDocs nav

### Implementation for User Story 9

- [X] T093 [P] [US9] Create POA&M Management guide at docs/guides/poam-management.md covering dashboard overview, creation, lifecycle, component linkage, remediation sync, scan import, trend analytics, export, and ticketing integration — following existing guide pattern (feature callout, overview, parameter tables, navigation instructions) per FR-020
- [X] T094 [P] [US9] Update persona guides with POA&M workflows: docs/guides/issm-guide.md (POA&M oversight, trend review, eMASS export), docs/getting-started/isso.md (POA&M creation, lifecycle management), docs/guides/engineer-guide.md (read-only view, task cascade), docs/guides/ao-quick-reference.md (trend reports, auth decisions), docs/guides/sca-guide.md (assessment-driven auto-creation) per FR-021
- [X] T095 [P] [US9] Update Agent Tool Catalog at docs/architecture/agent-tool-catalog.md with reference entries for all 18 new MCP tools and 3 updated tool entries — including parameter tables, response schemas, RBAC notes, and example invocations per FR-022
- [X] T096 [P] [US9] Update Tool Inventory at docs/reference/tool-inventory.md with POA&M tool category (Lifecycle, Component Linkage, Remediation Sync, Trend, Export, Bulk, Ticketing) per FR-022
- [X] T097 [P] [US9] Update Data Model reference at docs/architecture/data-model.md with PoamComponentLink, PoamHistoryEntry, TicketingIntegration, PoamTicketSync entities and PoamItem/RemediationTask extensions per FR-022
- [X] T098 [P] [US9] Update RMF phase guides: docs/rmf-phases/assess.md (finding → POA&M auto-creation), docs/rmf-phases/monitor.md (POA&M trend tracking), docs/rmf-phases/authorize.md (POA&M exports for auth packages) per FR-021
- [X] T099 [P] [US9] Update supporting references: docs/reference/glossary.md (POA&M lifecycle terms, cascade confirmation, bidirectional sync), docs/guides/nl-query-reference.md (POA&M natural language command examples), docs/guides/remediation-kanban.md (refocused scope, "Linked POA&M" column, click-to-open cards) per FR-021
- [X] T100 [US9] Update mkdocs.yml navigation to include POA&M Management guide under Guides section per SC-011

**Checkpoint**: All 7 documentation areas published and accessible from MkDocs navigation (SC-011)

---

## Phase 14: Polish & Cross-Cutting Concerns

**Purpose**: Testing, performance validation, security review, and final integration verification

- [X] T101 [P] Integration tests for POA&M REST endpoints in tests/Ato.Copilot.Tests.Integration/Poam/PoamEndpointTests.cs — cover CRUD happy path, pagination boundaries, lifecycle validation error paths, bulk operations, concurrency conflicts, and RBAC enforcement via WebApplicationFactory per Constitution III
- [X] T102 [P] Unit tests for all 18 new MCP tools in tests/Ato.Copilot.Tests.Unit/Tools/Poam/ — one test class per tool covering parameter validation, success path, and error path per Constitution III
- [X] T103 [P] Frontend component tests with Vitest for POA&M components in src/Ato.Copilot.Dashboard/src/components/poam/__tests__/ — cover PoamTable (pagination, filtering, sorting), PoamDetailDrawer (data display, concurrency conflict dialog), PoamLifecycleActions (transition rule enforcement), CascadeConfirmDialog (confirm/dismiss flows) per Constitution III
- [X] T104 [P] Run quickstart.md validation to verify build, Docker compose, and all 6 verification steps pass
- [X] T105 Performance optimization: verify POA&M page load < 3s (SC-001), bulk creation 100+ items < 10s (SC-002), MCP tool responses < 5s simple / < 30s complex (Constitution VIII)
- [X] T106 Security review: verify RBAC enforcement on all 21 endpoints, Key Vault credential handling (no secrets in DB), input validation/sanitization, optimistic concurrency on all writes, CascadeOrigin loop prevention
- [X] T107 Code cleanup: verify no POA&M UI duplication between Remediation.tsx and PoamManagement.tsx, remove dead code from Remediation refocus, validate all TypeScript types match API contracts

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — first MVP deliverable
- **US2 (Phase 4)**: Depends on Phase 2; integrates with US1 PoamCreateForm/PoamDetailDrawer
- **US3 (Phase 5)**: Depends on Phase 2; extends KanbanService (independent of US1 frontend)
- **US4 (Phase 6)**: Depends on Phase 2; extends PoamService with lifecycle rules
- **US4a (Phase 7)**: Depends on Phase 6 (lifecycle transition logic for cascade triggers)
- **US4b (Phase 8)**: Depends on Phase 7 (SyncIndicator, linked POA&M badge require sync infrastructure)
- **US5 (Phase 9)**: Depends on Phase 2; independent of other P1 stories (reads historical data)
- **US6 (Phase 10)**: Depends on Phase 2; independent of other stories (separate TicketingService)
- **US7 (Phase 11)**: Depends on Phase 2; independent of other stories (read-only export)
- **US8 (Phase 12)**: Depends on Phases 3–11 (all tools must be registered before chat validation)
- **US9 (Phase 13)**: Depends on Phases 3–11 (docs describe implemented behavior)
- **Polish (Phase 14)**: Depends on all desired user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) ─────────────► Phase 2 (Foundation) ─────┬──► Phase 3 (US1) ─── MVP
                                                          │
                                                          ├──► Phase 4 (US2) ─── can parallel with US3
                                                          │
                                                          ├──► Phase 5 (US3) ─── can parallel with US2
                                                          │
                                                          ├──► Phase 6 (US4) ──► Phase 7 (US4a) ──► Phase 8 (US4b)
                                                          │
                                                          ├──► Phase 9 (US5) ─── independent
                                                          │
                                                          ├──► Phase 10 (US6) ── independent
                                                          │
                                                          └──► Phase 11 (US7) ── independent
                                                          
All stories complete ──► Phase 12 (US8) ──► Phase 13 (US9) ──► Phase 14 (Polish)
```

### Within Each User Story

- Service/backend logic before API endpoints
- API endpoints before frontend components
- Core components before integration/wiring
- MCP tools can parallel with frontend (different files)
- Tests can parallel with implementation (different files)
- Story complete before moving to next priority

### Parallel Opportunities

**Phase 1**: T001+T002+T003 (different files), T004+T005 (different model files), T008 (independent frontend)

**Phase 2**: T011+T012+T013+T014 (different files, after T009/T010)

**Phase 3**: T015+T016 (routing + nav, different files), T018+T019 (summary cards + heatbar, different files), T024+T025+T026 (different tools), all tools [P]

**Phase 4**: T028+T031+T032+T033 (different files)

**Phase 5**: T039+T040 (different tool files)

**Phase 6**: T047+T048+T049+T050+T051 (different tool/test files)

**Phase 7**: T057+T058+T059+T061 (different tool/test files)

**Phase 9**: T073+T074 (different tool files)

**Phase 10**: T076+T077 (different provider files), T080+T081+T082+T084 (different tool/test files)

**Phase 13**: T093-T099 (all different doc files, all parallel)

**Phase 14**: T101+T102+T103+T104 (all different test/validation files)

---

## Parallel Example: User Story 1

```bash
# Step 1: Route + nav setup (parallel)
T015: "Add POA&M routes to App.tsx"
T016: "Add nav items to SystemLayout.tsx"

# Step 2: Page shell + summary cards + heatbar + metrics endpoint (parallel after T015/T016)
T017: "Create PoamManagement.tsx page shell"
T018: "Create PoamSummaryCards.tsx"
T019: "Create PoamSeverityHeatbar.tsx"
T023: "Add metrics endpoints to DashboardEndpoints.cs"

# Step 3: Table + detail drawer + create form (sequential, compose on page)
T020: "Create PoamTable.tsx"
T021: "Create PoamDetailDrawer.tsx (with concurrency conflict dialog)"
T022: "Create PoamCreateForm.tsx"

# Step 4: MCP tools (parallel with Step 3, different files)
T024: "Create GetPoamTool.cs"
T025: "Update compliance_create_poam tool"
T026: "Update compliance_list_poam tool"

# Step 5: Integration (after Steps 2-4)
T027: "Integrate all components + heatbar into PoamManagement.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (entities, enums, DB, types)
2. Complete Phase 2: Foundational (service, API, client, hooks, RBAC, unit tests)
3. Complete Phase 3: User Story 1 (page, table, cards, heatbar, drawer, form, tools)
4. **STOP and VALIDATE**: Navigate to /systems/:id/poam — page loads with data, CRUD works, MCP tools callable
5. Deploy/demo if ready — this is the MVP deliverable

### Incremental Delivery (P1 Stories)

1. Setup + Foundation → Core infrastructure ready
2. US1 → POA&M page with CRUD → **Deploy (MVP)**
3. US2 → Component linkage on POA&M items → Deploy
4. US3 → Scan import auto-generates POA&Ms → Deploy
5. US4 → Full lifecycle with audit trail → Deploy
6. US4a → Bidirectional remediation sync → Deploy
7. US4b → Remediation page refocused → Deploy
8. **All P1 complete** — core feature fully operational

### P2 + P3 Stories (Post-MVP)

9. US5 → Trend analytics + PDF export → Deploy
10. US6 → Jira/ServiceNow integration → Deploy
11. US7 → eMASS/OSCAL export → Deploy
12. US8 → Chat-driven operations (P3) → Deploy
13. US9 → Documentation updates → Deploy
14. Polish → Testing, performance, security, cleanup → Final release

### Parallel Team Strategy

With multiple developers after Foundation completes:
- **Developer A**: US1 (MVP page) → US4 (lifecycle) → US4a (sync) → US4b (refocus)
- **Developer B**: US2 (components) → US5 (trends) → US7 (export)
- **Developer C**: US3 (auto-generation) → US6 (ticketing) → US8 (chat)
- **Developer D**: US9 (documentation) → Polish (tests + validation)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Foundation
- US4 → US4a → US4b is the only sequential chain within P1 stories
- Test tasks are distributed across phases per Constitution III (Testing Standards)
- MCP tool tasks include registration in ComplianceMcpTools
- All API endpoints follow existing DashboardEndpoints.cs patterns (PaginatedResponse<T>, ErrorResponse)
- All MCP tools extend BaseTool with `compliance_` prefix and JSON `{ status, data, metadata }` response
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
