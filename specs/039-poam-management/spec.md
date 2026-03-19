# Feature Specification: POA&M Management

**Feature Branch**: `039-poam-management`  
**Created**: 2026-03-18  
**Status**: Draft  
**Input**: User description: "POA&M Management page — dedicated page for Plan of Action and Milestones tracking, import vulnerability scans, link POA&M items to system components, track remediation deadlines, trend reporting, Jira/ServiceNow integration, deviation type tracking"

## Assumptions

- The existing `PoamItem` entity (with milestones, deviation links, finding links, and remediation task links) remains the core data model. New fields are additive, not breaking.
- Component linkage reuses the `SystemComponent` entity hierarchy already present in the data model (Feature 025 — HW/SW Inventory). However, the system-scoped Component Inventory page (`ComponentInventory.tsx`) is not currently routed or navigable in the dashboard — it exists as an unlinked page. This feature must wire it up as a prerequisite so users can browse and manage system components before linking them to POA&M items.
- The Component Inventory page must be added to the system-scoped navigation (SystemLayout) at `/systems/:id/components` and registered in the application router. This ensures users can access the inventory from the system sidebar alongside Boundaries, Capability Coverage, and other system-level pages.
- Deviation tracking (false positive, risk acceptance, waiver) is handled by the Deviation entity from Feature 035. This page surfaces deviation state alongside POA&M items but does not duplicate deviation management.
- The existing Remediation page (kanban + table) currently serves double duty as both task manager and POA&M tracker — it shows POA&M summary cards, severity heatbar, aging charts, and a table with POA&M-centric columns (milestones, deviation status, etc.). With the introduction of the dedicated POA&M Management page, the Remediation page must be refocused to manage remediation tasks only. POA&M-specific UI (summary cards for POA&M counts, aging charts, milestone columns) moves to the new POA&M page. The Remediation page retains: task pipeline, kanban board, task table, and task-level analytics. The table adds a "Linked POA&M" column showing the linked POA&M status badge (clickable to navigate). Kanban cards gain click-to-open-detail behavior (opening the same task detail drawer available from the table). Both pages share a tight bidirectional relationship: creating or linking a kanban remediation task to a POA&M item establishes a live sync where status changes, severity updates, and completion dates propagate between the two entities automatically. The existing `RemediationTask.PoamItemId` and `PoamItem.RemediationTaskId` foreign keys already exist but currently act as passive references — this feature must activate them with bidirectional metadata sync and cascade behavior. Cascade confirmation model: dashboard UI prompts the user for confirmation before applying cascade changes to the linked entity; API/MCP tool calls auto-apply cascades without prompts, recording full audit trail.
- Vulnerability scan import leverages existing scan import infrastructure (ACAS/Nessus from Feature 026, STIG from Feature 017, Prisma Cloud from Feature 019). This page adds the ability to auto-generate POA&M items from imported findings.
- When remediation tasks are created from assessment results (via `CreateBoardFromAssessmentAsync`), the system must also auto-create linked POA&M items for each remediation task. This means the assessment → finding → remediation task pipeline now produces a POA&M item alongside each task, with both entities linked bidirectionally from creation. The POA&M item is pre-populated from the finding (weakness, control ID, CAT severity, source) and the remediation task stores the POA&M's ID (and vice versa). This ensures every remediation item has formal POA&M tracking from the moment it's created.
- External ticketing integration (Jira/ServiceNow) uses webhook-based sync rather than real-time bidirectional API polling, to avoid API rate limits and credential sprawl. Ticketing credentials (API tokens, service account keys) are stored in Azure Key Vault — the `TicketingIntegration` entity stores a Key Vault secret URI reference, never the credential itself. This ensures encryption at rest, access auditing, and credential rotation without redeployment.
- POA&M trend data is derived from periodic compliance snapshots already captured by the Compliance Watch subsystem (Feature 005).
- Users access POA&M Management from two places: a cross-system view at `/poam` (org-level) and a system-scoped view at `/systems/:id/poam`.
- The POA&M navigation item is positioned directly below the Remediation item in the sidebar (both org-level and system-scoped). This groups the two related workflows together — Remediation for active fix execution, POA&M for formal tracking and compliance reporting.
- The POA&M Management page has a page header with a title ("POA&M Management"), descriptive subtext explaining the page purpose (e.g., "Track and manage Plan of Action & Milestones across your systems"), and an "Add POA&M" action button in the header that opens the POA&M creation form.
- POA&M operations follow role-based access control: ISSO, ISSM, AO, and Compliance Officer can create, update, and delete POA&M items directly. Engineers have read-only access to POA&M data but can modify linked remediation tasks — changes to remediation tasks (status, severity, due date) cascade to linked POA&M items via bidirectional sync, giving engineers an indirect update path. The "Add POA&M" button and status-change actions are hidden for read-only roles.

---

## Clarifications

### Session 2026-03-18

- Q: Which roles can create/update/delete POA&M items vs. read-only? → A: ISSO, ISSM, AO, Compliance Officer can create/update/delete. Engineer gets read-only POA&M access but can update remediation tasks, with changes cascading to linked POA&Ms via bidirectional sync.
- Q: How are external ticketing credentials (Jira/ServiceNow API tokens) stored? → A: Azure Key Vault. The `TicketingIntegration` entity stores a Key Vault secret URI reference, not the credential itself.
- Q: How does the POA&M table handle large result sets? → A: Server-side pagination with 25 items per page default, page size selector (25/50/100).
- Q: How should bidirectional sync cascades handle confirmation? → A: UI cascades prompt the user for confirmation before applying; API/MCP cascades auto-apply with full audit trail (no prompt). This gives interactive users a safety net while keeping programmatic workflows frictionless.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — POA&M Dashboard Overview (Priority: P1)

An ISSM navigates to the POA&M Management page (via the "POA&M" nav item positioned below "Remediation" in the sidebar) and sees a page header with the title "POA&M Management", descriptive subtext ("Track and manage Plan of Action & Milestones across your systems"), and an "Add POA&M" button. Below the header, summary cards show total open items, overdue count, items by CAT severity, and items expiring within 30 days. A severity heatbar visualizes the distribution. Below, a filterable table lists every POA&M item with columns for control ID, weakness, severity, status, component, POC, due date, days remaining, milestone progress, and linked deviation type. The ISSM filters by system, severity, and status to focus on CAT I overdue items that need immediate attention.

**Why this priority**: The overview page is the primary interface. Without it, there is no dedicated POA&M tracking surface — users must rely on the combined Remediation page which mixes fix execution with formal tracking.

**Independent Test**: Navigate to `/poam`, verify summary cards display accurate counts, filter by CAT I + Overdue, confirm the table narrows to matching items, click a row to open the detail drawer.

**Acceptance Scenarios**:

1. **Given** an ISSM with access to 3 systems having 45 total open POA&M items, **When** they navigate to the POA&M Management page, **Then** summary cards display: total open (45), overdue count, CAT I count, items expiring within 30 days, and average days to close.
2. **Given** the POA&M table, **When** the user selects "CAT I" severity filter and "Overdue" status filter, **Then** only POA&M items matching both criteria are displayed.
3. **Given** the POA&M table, **When** the user clicks a row, **Then** a detail drawer opens showing full weakness description, component linkage, milestones with progress, linked finding, linked remediation task, deviation status (if any), audit history, and action buttons.
4. **Given** the page with multiple systems, **When** the user selects a specific system from the system filter, **Then** only POA&M items for that system are shown and summary cards update accordingly.
5. **Given** the POA&M table, **When** the user types a search query (e.g., "AC-2" or "firewall"), **Then** the table filters to items matching the query against control ID, weakness text, or component name.
6. **Given** an ISSM with access to a system with 150 open POA&M items, **When** the page loads, **Then** the table displays the first 25 items with pagination controls showing page 1 of 6, and a page size selector offering 25/50/100 items per page.

---

### User Story 2 — Component-Linked POA&M Creation (Priority: P1)

An ISSO creates a new POA&M item and links it to one or more system components (e.g., "API Gateway — api-gw-prod-01" or "Database Server — sql-primary"). The component picker shows the system's registered HW/SW inventory. When a component is linked, the POA&M detail view shows which specific assets are affected, enabling precise remediation scoping. Components linked to multiple POA&Ms show an aggregate risk indicator on the component inventory page.

**Why this priority**: Linking POA&Ms to specific components is the key differentiator from the existing flat POA&M table. It enables asset-level risk analysis and answers the question "what is the risk posture of this specific component?"

**Prerequisite — Wire Component Inventory Page**: Before component-linked POA&M creation can work, the existing `ComponentInventory.tsx` page must be made accessible in the dashboard. This requires:
- Adding a route for `/systems/:id/components` in the application router
- Adding a "Components" navigation item to the system sidebar (SystemLayout) between "Boundaries" and "Capability Coverage"
- This gives users a dedicated system-scoped view to browse, search, and manage the components that POA&M items will link to

**Independent Test**: Navigate to a system's "Components" tab and verify the inventory loads. Then create a POA&M item, link it to a component from the inventory picker, verify the component appears in the POA&M detail, and verify the component's inventory page shows the linked POA&M.

**Acceptance Scenarios**:

1. **Given** a system with assigned components, **When** the user clicks "Components" in the system sidebar, **Then** the Component Inventory page loads at `/systems/:id/components` showing components grouped by boundary with search, filter, and CRUD operations.
2. **Given** the POA&M creation form, **When** the user selects the "Link Components" field, **Then** a searchable picker displays the system's registered components from the HW/SW inventory.
3. **Given** a POA&M item linked to component "api-gw-prod-01", **When** the user views the POA&M detail drawer, **Then** the linked component(s) are displayed with component type, name, and current risk indicator.
4. **Given** a component linked to 3 open POA&M items (1 CAT I, 2 CAT II), **When** viewing the component inventory page at `/systems/:id/components`, **Then** the component displays an aggregate risk badge (based on highest CAT severity of linked open POA&Ms).
5. **Given** the POA&M creation form, **When** the user does not link any component, **Then** the POA&M item is created successfully (component linkage is optional for backwards compatibility).

---

### User Story 3 — Auto-Generate POA&Ms from Assessments and Scan Imports (Priority: P1)

When remediation tasks are created from assessment results, the system automatically creates a linked POA&M item for each task. The POA&M is pre-populated from the assessment finding (weakness description, control ID, CAT severity, source, POC) and bidirectionally linked to the remediation task from creation. This means every new remediation task enters the system with formal POA&M tracking — no manual step required.

Separately, after importing a vulnerability scan (ACAS/Nessus, STIG, or Prisma Cloud), the system presents the user with a list of new findings that do not already have linked POA&M items. The user can select findings individually or in bulk and click "Create POA&M Items" to auto-generate POA&M entries. Duplicate detection prevents creating POA&M items for findings that already have active POA&M entries.

**Why this priority**: Assessments and scans are the two primary sources of findings. Auto-creating POA&M items from both ensures every weakness has formal tracking from discovery, without manual data entry.

**Independent Test**: Run an assessment that produces 10 findings, create a remediation board, and verify all 10 remediation tasks have linked POA&M items. Separately, import a scan with 5 new findings, generate POA&M items, and confirm they appear correctly.

**Acceptance Scenarios**:

1. **Given** a completed assessment with 12 open findings, **When** the user clicks "Create Remediation Board", **Then** 12 remediation tasks are created AND 12 linked POA&M items are auto-created, each pre-populated with the finding's weakness, control ID, CAT severity, and source. Each task stores the POA&M ID and each POA&M stores the task ID.
2. **Given** an assessment finding that already has an existing active POA&M item, **When** the remediation board is created, **Then** the new task is linked to the existing POA&M item rather than creating a duplicate.
3. **Given** an imported ACAS scan with 15 findings, 10 of which lack POA&M entries, **When** the import completes, **Then** the system displays a prompt: "10 findings have no POA&M items. Create them now?"
4. **Given** the post-import POA&M generation view, **When** the user selects 5 findings and clicks "Create POA&M Items", **Then** 5 new POA&M items are created with fields pre-populated from finding metadata (weakness, control ID, severity, source) and a link back to the originating finding.
5. **Given** a finding that already has an active (non-completed) POA&M item, **When** the auto-generation view is displayed, **Then** that finding is grayed out with a "POA&M exists" label and cannot be selected.
6. **Given** bulk POA&M creation for 50+ findings (from either assessment or scan), **When** the creation runs, **Then** all items are created within 5 seconds and the user sees a confirmation summary with counts by severity.
7. **Given** a remediation board created from an assessment, **When** the user views the kanban board, **Then** each task card shows a "POA&M Linked" indicator with the POA&M status badge.

---

### User Story 4 — POA&M Lifecycle Management (Priority: P1)

An ISSO updates a POA&M item's status through its lifecycle: Ongoing → Delayed → Completed or Ongoing → Risk Accepted. Status changes require context — moving to Delayed requires an explanation and a revised completion date; moving to Completed triggers a validation check against linked findings; moving to Risk Accepted requires linking a deviation record (Feature 035). The POA&M detail drawer shows a timeline of all status changes with actor, timestamp, and comments.

**Why this priority**: Lifecycle tracking is the core purpose of a POA&M — it must be possible to track an item from discovery through resolution with a complete audit trail.

**Independent Test**: Transition a POA&M through Ongoing → Delayed (with explanation) → Ongoing (with revised date) → Completed (with finding validation), and verify each transition is recorded in the audit timeline.

**Acceptance Scenarios**:

1. **Given** an Ongoing POA&M item, **When** the ISSO clicks "Mark Delayed", **Then** a dialog requires a delay reason and revised scheduled completion date before the status changes.
2. **Given** a Delayed POA&M item, **When** the ISSO provides a new completion date and clicks "Resume", **Then** the status reverts to Ongoing with the updated date and a history entry recording the delay period.
3. **Given** an Ongoing POA&M item, **When** the ISSO clicks "Mark Completed", **Then** the system checks whether the linked finding (if any) is resolved. If resolved, the status changes to Completed with the actual completion date. If still open, a warning appears: "Linked finding is still open — complete anyway?"
4. **Given** an Ongoing POA&M item, **When** the ISSO clicks "Risk Accepted", **Then** the system requires linking a Deviation record of type Risk Acceptance before allowing the transition.
5. **Given** any POA&M item, **When** the user views the detail drawer's "History" tab, **Then** all status changes, comment additions, and field edits are displayed in reverse chronological order with actor, timestamp, and details.

---

### User Story 4a — Remediation-POA&M Bidirectional Sync (Priority: P1)

An ISSO views a POA&M item and clicks "Create Remediation Task" to generate a linked kanban task pre-populated with the POA&M's weakness, control ID, severity, due date, and POC. Alternatively, from the Remediation kanban board, a user clicks "Link to POA&M" on an existing task to associate it with a POA&M item. Once linked, changes flow bidirectionally: moving a kanban task to "Done" prompts the POA&M to transition to Completed; marking a POA&M as "Risk Accepted" updates the linked task's status; changing severity or due date on either side propagates to the other. A sync indicator on both the POA&M detail drawer and the kanban task card shows the link status and last sync time.

**Why this priority**: POA&M tracking and remediation execution are two sides of the same coin. Without sync, users must manually keep both in sync — leading to drift, stale data, and missed deadlines. This is a core workflow, not a nice-to-have.

**Independent Test**: Create a remediation task from a POA&M item, verify fields are pre-populated. Move the task to "Done" on the kanban board, verify the POA&M status prompts for completion. Change the POA&M due date, verify the task's due date updates.

**Acceptance Scenarios**:

1. **Given** a POA&M item with no linked remediation task, **When** the user clicks "Create Remediation Task" in the POA&M detail drawer, **Then** a new kanban task is created on the system's remediation board pre-populated with: title from weakness, control ID, CAT severity mapped to task severity, scheduled completion date as due date, and POC as assignee. Both entities store each other's ID.
2. **Given** an existing remediation task with no linked POA&M, **When** the user clicks "Link to POA&M" on the task card, **Then** a searchable POA&M picker shows open items for the same system, and selecting one establishes the bidirectional link.
3. **Given** a linked remediation task moved to "Done" in the dashboard UI, **When** the kanban board saves the status change, **Then** a confirmation dialog prompts: "Linked remediation task completed — mark POA&M as Completed?" If confirmed, the POA&M transitions to Completed with the actual completion date set to now and a history entry recording the cascade. If dismissed, only the task status changes; the POA&M remains unchanged.
4. **Given** a linked POA&M item marked as "Risk Accepted" in the dashboard UI, **When** the status change saves, **Then** a confirmation dialog prompts: "Update linked remediation task to Risk Accepted?" If confirmed, the linked task is moved to a "Won't Fix" or "Risk Accepted" column (if available) or its status is updated to Closed with a reason of "Risk Accepted — linked POA&M deviation". Via API/MCP, the cascade auto-applies with audit trail.
5. **Given** a linked POA&M item whose due date is changed from June 15 to July 30 in the dashboard UI, **When** the change saves, **Then** a confirmation prompts: "Update linked task due date to July 30?" If confirmed, the linked remediation task's due date updates and a history entry records the cascade source. Via API/MCP, the cascade auto-applies with audit trail.
6. **Given** a linked remediation task whose severity is changed from High to Critical, **When** the change saves, **Then** the linked POA&M's CAT severity is updated accordingly (Critical → CAT I) and a history entry records the cascade.
7. **Given** a linked pair, **When** the user views either the POA&M detail drawer or the kanban task card, **Then** a sync indicator shows: linked entity name, link status (synced/conflict), and last sync timestamp. Clicking the indicator navigates to the linked entity.
8. **Given** a user unlinking a remediation task from a POA&M, **When** they click "Unlink", **Then** both FKs are cleared, sync stops, and a history entry records the unlink on both entities. Neither entity is deleted.

---

### User Story 4b — Remediation Page Refocus (Priority: P1)

With the new POA&M Management page handling formal POA&M tracking, the existing Remediation page (`Remediation.tsx`) is refocused to manage remediation tasks only. The page currently shows POA&M summary cards (Open POA&Ms, Overdue, CAT I Open), a severity heatbar, POA&M aging chart, and a table with POA&M-centric columns (milestones, deviation type). These POA&M-specific elements move to the new POA&M Management page. The Remediation page retains: task pipeline status bar, kanban board with drag-and-drop, task table, and task-level summary cards (Total Tasks, tasks by status). The task table gains a "Linked POA&M" column that displays the linked POA&M item's status badge (Ongoing, Delayed, Completed, Risk Accepted) — clicking the badge navigates to the POA&M detail on the POA&M Management page. Kanban cards gain a click handler: clicking a card (not dragging) opens the task detail drawer, the same drawer currently accessible only via the table's "Detail" button. This gives users quick access to task details, linked POA&M status, and action buttons without switching to table view.

**Why this priority**: The Remediation page must be updated as part of the POA&M page introduction to avoid duplicate UI surfaces showing the same data. Without this, users see POA&M metrics in two places with potentially different refresh cadences, causing confusion.

**Independent Test**: Navigate to the Remediation page, verify POA&M summary cards and aging chart are removed. Verify the task table shows a "Linked POA&M" column with clickable badges. Click a kanban card and verify the detail drawer opens.

**Acceptance Scenarios**:

1. **Given** the Remediation page after feature deployment, **When** the user navigates to it, **Then** POA&M-specific summary cards (Open POA&Ms, Overdue, CAT I Open, Avg Days to Close) are no longer displayed. Task-specific summary cards remain (Total Tasks, by-status counts).
2. **Given** the Remediation page after feature deployment, **When** the user navigates to it, **Then** the severity heatbar showing CAT breakdown and the POA&M aging chart (0-30, 31-60, 61-90, 90+ days) are no longer displayed. The task pipeline status bar remains.
3. **Given** the task table, **When** the user views the columns, **Then** a "Linked POA&M" column is present showing the linked POA&M's status badge (color-coded by status) for tasks that have a `poamItemId`, or "—" for unlinked tasks.
4. **Given** a task with a linked POA&M showing an "Ongoing" badge in the table, **When** the user clicks the badge, **Then** the browser navigates to the POA&M Management page with the linked POA&M's detail drawer open.
5. **Given** the kanban board, **When** the user clicks (not drags) a task card, **Then** the task detail drawer opens on the right side showing full task metadata, linked POA&M status, linked finding, assignee, and action buttons.
6. **Given** the kanban board, **When** the user drags a task card, **Then** the existing drag-and-drop behavior works as before — the card moves to the target column and the click handler does not fire.
7. **Given** the task detail drawer opened from a kanban card, **When** the user views the "Linked POA&M" section, **Then** the POA&M status badge and a "View POA&M" link are displayed, navigating to the POA&M detail on the POA&M Management page.
8. **Given** the task table, **When** the user views milestones and deviation columns, **Then** these POA&M-specific columns are removed from the task table (milestones and deviations are tracked on the POA&M Management page).

---

### User Story 5 — Trend Reporting and Analytics (Priority: P2)

An AO navigates to the POA&M trend dashboard to see how the organization's POA&M posture has changed over time. Charts include: open POA&M count over time (line chart), time-to-close distribution (histogram), aging breakdown (stacked bar by severity), and closure rate per month (bar chart). Filters allow drilling down by system, severity, and date range. The AO exports a trend report as a PDF or Excel for inclusion in authorization decision packages.

**Why this priority**: Trend data transforms POA&M from a tracking list into a strategic decision-making tool. AOs and ISSMs need historical context to assess whether the organization's risk posture is improving. Depends on core POA&M data from P1 stories.

**Independent Test**: Navigate to the trend dashboard, verify charts render with historical data, apply a date range filter, and export a PDF report.

**Acceptance Scenarios**:

1. **Given** a system with 6 months of POA&M history, **When** the user views the trend dashboard, **Then** the "Open POA&Ms Over Time" chart shows monthly data points for the past 6 months.
2. **Given** the trend dashboard, **When** the user selects a 90-day date range, **Then** all charts update to show only data within that range.
3. **Given** the trend dashboard, **When** the user clicks "Export Report", **Then** a PDF is generated containing all visible charts, summary statistics, and the applied filters.
4. **Given** POA&M items across multiple systems, **When** the user filters by a specific system, **Then** trend charts reflect only that system's data.
5. **Given** the aging breakdown chart, **When** the user hovers over a bar segment, **Then** a tooltip shows the exact count and percentage for that severity/age bucket.

---

### User Story 6 — External Ticketing Integration (Priority: P2)

A Compliance Officer configures a Jira or ServiceNow integration at the system level. Once configured, POA&M items can be synced to the external ticketing system as issues/incidents. The sync is bidirectional: status changes in Jira/ServiceNow are reflected back in the POA&M item, and vice versa. The integration configuration specifies project/queue mapping, field mapping, and sync frequency. A sync status indicator shows the last sync time and any sync errors.

**Why this priority**: Many organizations already use Jira or ServiceNow for work tracking. Integration prevents dual-entry and lets remediation teams work in their preferred tool while the ISSM tracks formal POA&M status in the dashboard.

**Independent Test**: Configure a Jira integration, sync a POA&M item, update the Jira issue status, trigger a sync, and verify the POA&M status reflects the change.

**Acceptance Scenarios**:

1. **Given** the system settings page, **When** the user clicks "Configure Jira Integration", **Then** a form collects: Jira URL, project key, issue type, authentication credentials (API token stored to Azure Key Vault on save — never persisted in the database), and field mapping for severity/status.
2. **Given** a configured integration, **When** the user opens a POA&M detail and clicks "Sync to Jira", **Then** a Jira issue is created with mapped fields and the POA&M item stores the external ticket reference.
3. **Given** a synced POA&M item whose Jira issue was moved to "Done", **When** the next sync cycle runs, **Then** the POA&M status updates to reflect the external change and a history entry records the sync event.
4. **Given** a sync failure (e.g., network error, invalid credentials), **When** the sync runs, **Then** the sync status indicator shows the error, the POA&M item retains its last known state, and a notification is raised.
5. **Given** the integration settings, **When** the user configures ServiceNow instead of Jira, **Then** the same field mapping and sync workflow apply with ServiceNow-specific connection parameters (instance URL, table, credentials).
6. **Given** multiple POA&M items, **When** the user clicks "Bulk Sync", **Then** all unsynced or changed items are pushed to the external system in a single batch operation.

---

### User Story 7 — POA&M Export and Compliance Reporting (Priority: P2)

An ISSM exports POA&M data in eMASS-compatible Excel format for submission to the authorizing official or upload to eMASS. The export includes all eMASS POA&M template columns (weakness, source, control number, severity, POC, milestones, status, completion dates, deviation information). The ISSM can also export in OSCAL POA&M JSON format for interoperability with other GRC tools.

**Why this priority**: POA&M export is an existing capability (Feature 015), but the dedicated POA&M page provides a better user experience for export — users can filter, review, and then export exactly the items they need rather than exporting everything.

**Independent Test**: Filter POA&M items to CAT I + Ongoing, click "Export eMASS", verify the downloaded Excel file contains only filtered items with all eMASS template columns populated.

**Acceptance Scenarios**:

1. **Given** the POA&M table with active filters (e.g., CAT I only), **When** the user clicks "Export", **Then** a dialog offers format choices: eMASS Excel, OSCAL JSON, and CSV.
2. **Given** selecting eMASS Excel export, **When** the export runs, **Then** the downloaded file matches the 24-column eMASS POA&M template structure with deviation justification and type columns populated where applicable.
3. **Given** selecting OSCAL JSON export, **When** the export runs, **Then** the output conforms to the NIST OSCAL POA&M schema with all required elements.
4. **Given** filtered POA&M data, **When** the user selects "Export All (unfiltered)", **Then** all POA&M items for the selected system(s) are included regardless of active filters.

---

### User Story 8 — Chat-Driven POA&M Operations (Priority: P3)

An engineer or ISSO uses the dashboard chat to perform POA&M operations via natural language. They can ask "Show me all overdue CAT I POA&Ms" or "Create a POA&M for AC-2 on the API Gateway" or "What's the trend for POA&M closure this quarter?" The AI calls existing MCP tools (`compliance_create_poam`, `compliance_list_poam`) and new trend analysis tools to respond with formatted tables, charts, and actionable suggestion cards.

**Why this priority**: Chat integration extends POA&M management to all three surfaces (dashboard, Teams, VS Code) but depends on the core page and data model being in place.

**Independent Test**: Ask the chat "Show me all overdue POA&Ms" and verify a formatted table is returned with correct data matching the POA&M Management page.

**Acceptance Scenarios**:

1. **Given** the dashboard chat with system context, **When** the user asks "Show me overdue POA&Ms", **Then** the AI calls `compliance_list_poam` with overdue filter and returns a formatted table.
2. **Given** the chat, **When** the user says "Create a POA&M for AC-2 on the API Gateway, CAT II, due in 90 days, assigned to John Smith", **Then** the AI calls `compliance_create_poam` with parsed parameters including component linkage and confirms creation.
3. **Given** the chat, **When** the user asks "What's our POA&M closure trend this quarter?", **Then** the AI returns a summary with closure count, average days to close, and comparison to the previous quarter.

---

### User Story 9 — Documentation Updates (Priority: P2)

As each POA&M Management capability ships, the user-facing documentation must be updated so that ISSMs, ISSOs, Engineers, AOs, and SCAs can discover, learn, and reference the new workflows without relying on tribal knowledge. Documentation updates span seven areas:

1. **New Guide — POA&M Management** (`docs/guides/poam-management.md`): A dedicated feature guide covering the POA&M dashboard overview, creating and editing POA&M items, component linkage, lifecycle transitions, bidirectional remediation sync, scan-import auto-generation, trend analytics, export, and external ticketing integration. Follows the existing guide pattern (feature callout, overview, section-per-workflow, parameter tables, navigation instructions).
2. **Persona Guides**: Update `docs/guides/issm-guide.md` (POA&M oversight, trend review, export for authorization packages), `docs/getting-started/isso.md` and persona workflow sections (POA&M creation, lifecycle management, remediation task linking), `docs/guides/engineer-guide.md` (read-only POA&M view, remediation task updates that cascade), `docs/guides/ao-quick-reference.md` (trend reports, authorization decision support), and `docs/guides/sca-guide.md` (assessment-driven POA&M auto-creation).
3. **Agent Tool Catalog** (`docs/architecture/agent-tool-catalog.md`): Add reference entries for all 18 new MCP tools and update 3 existing tool entries with new parameters, including parameter tables, response schemas, RBAC notes, and example invocations.
4. **Tool Inventory** (`docs/reference/tool-inventory.md`): Add all new POA&M tools organized by category (Lifecycle, Component Linkage, Remediation Sync, Trend, Export, Bulk, Ticketing).
5. **Data Model** (`docs/architecture/data-model.md`): Document `PoamComponentLink` (new), `TicketingIntegration` (new), `PoamTicketSync` (new), and updates to `PoamItem` and `RemediationTask` entities for bidirectional sync.
6. **RMF Phase Guides**: Update `docs/rmf-phases/assess.md` (finding → POA&M auto-creation), `docs/rmf-phases/monitor.md` (POA&M trend tracking, continuous monitoring), and `docs/rmf-phases/authorize.md` (POA&M trend exports for authorization packages).
7. **Supporting References**: Update `docs/reference/glossary.md` (POA&M lifecycle terms, cascade confirmation, bidirectional sync), `docs/guides/nl-query-reference.md` (POA&M natural language command examples), and `docs/guides/remediation-kanban.md` (refocused scope, "Linked POA&M" column, click-to-open kanban cards).

**Why this priority**: Documentation is essential for user adoption but does not block core functionality. Shipping after P1 features ensures the docs describe the actual shipped behavior rather than speculative designs.

**Independent Test**: For each documentation area, verify the page renders correctly in MkDocs, contains accurate content matching the implemented feature, and is reachable from the MkDocs nav.

**Acceptance Scenarios**:

1. **Given** the documentation site, **When** a user navigates to Guides → POA&M Management, **Then** a comprehensive guide is displayed covering dashboard overview, creation, lifecycle, component linkage, remediation sync, scan import, trend analytics, export, and ticketing integration.
2. **Given** the ISSM guide, **When** a user reads the POA&M section, **Then** it includes a workflow for reviewing POA&M posture, filtering overdue items, exporting eMASS reports, and reviewing trend data for authorization packages.
3. **Given** the ISSO getting-started page, **When** a new ISSO reads the guide, **Then** it includes POA&M creation as part of the first-time workflow with example tool invocations.
4. **Given** the Agent Tool Catalog, **When** a user searches for POA&M tools, **Then** all 18 new tools and 3 updated tools have complete reference entries with parameters, response schemas, RBAC requirements, and example invocations.
5. **Given** the Data Model documentation, **When** a developer reviews entity relationships, **Then** `PoamComponentLink`, `TicketingIntegration`, and `PoamTicketSync` entities are documented with field definitions and relationship diagrams.
6. **Given** the Remediation Kanban guide, **When** a user reads it after feature deployment, **Then** it reflects the refocused scope (task-only), documents the "Linked POA&M" column, and describes click-to-open kanban card behavior.
7. **Given** the NL Query Reference, **When** a user looks up POA&M commands, **Then** example queries are listed: "Show overdue POA&Ms", "Create a POA&M for AC-2", "What's the POA&M closure trend?", "Link POA&M 42 to component api-gw-prod-01".
8. **Given** the MkDocs navigation, **When** a user browses the site, **Then** the POA&M Management guide appears under Guides and all updated pages are accessible from their existing nav locations.

---

### Edge Cases

- **Orphaned POA&Ms**: A POA&M item's linked finding is deleted (e.g., after re-import). The POA&M retains its data but shows a "Finding removed" indicator; it does not auto-delete.
- **Duplicate scan imports**: Re-importing the same scan does not create duplicate POA&M items. The system matches on finding reference + control ID + component to detect duplicates.
- **Component decommissioning**: When a linked component is decommissioned from the inventory, the POA&M item shows a "Component decommissioned" badge and prompts the user to close or reassign.
- **Concurrent status updates**: Two users updating the same POA&M simultaneously. The system uses optimistic concurrency — the second save displays a conflict message with the option to reload and re-apply.
- **Remediation-POA&M cascade conflicts**: A user moves a kanban task to "Done" while another user simultaneously marks the linked POA&M as "Delayed". The system detects the conflict via optimistic concurrency and presents both changes for resolution.
- **Circular cascade prevention**: A status change on a POA&M cascades to a remediation task, which must not cascade back to the POA&M. The system uses a cascade origin flag to prevent infinite loops.
- **Unlinked remediation tasks**: Deleting a POA&M item does not delete the linked remediation task — the task's `PoamItemId` is set to null and a history entry records the orphaning.
- **Ticketing sync conflicts**: External ticket status conflicts with POA&M status (e.g., Jira says "Done" but POA&M says "Ongoing"). The system flags the conflict and requires manual resolution rather than silently overwriting.
- **Large-scale POA&M generation**: Importing a scan with 500+ findings. The system handles batch creation with progress indication and does not time out.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a dedicated POA&M Management page accessible from the main navigation at `/poam` (cross-system) and `/systems/:id/poam` (system-scoped). The "POA&M" nav item MUST be positioned directly below the "Remediation" nav item in the sidebar. The page MUST display a header with title ("POA&M Management"), descriptive subtext, and an "Add POA&M" action button that opens the POA&M creation form.
- **FR-001a**: System MUST wire the existing Component Inventory page into the dashboard by adding a route at `/systems/:id/components` and a "Components" navigation item in the system sidebar, positioned between "Boundaries" and "Capability Coverage".
- **FR-002**: System MUST display summary cards showing total open POA&Ms, overdue count, CAT I count, items expiring within 30 days, and average days to close.
- **FR-003**: System MUST display a sortable, filterable, server-side paginated table of POA&M items with columns: control ID, weakness, severity, status, component(s), POC, due date, days remaining, milestone progress, deviation type, and external ticket reference. Default page size is 25 items with a page size selector (25/50/100).
- **FR-004**: System MUST support filtering by system, status, CAT severity, overdue flag, component, weakness source, and free-text search.
- **FR-005**: System MUST support linking POA&M items to one or more system components from the HW/SW inventory.
- **FR-006**: System MUST auto-generate POA&M items from imported vulnerability scan findings with duplicate detection based on finding reference + control ID + component.
- **FR-006a**: System MUST auto-create a linked POA&M item for each remediation task generated from assessment results, pre-populated from the finding and bidirectionally linked to the task from creation. If an active POA&M already exists for the finding, the task links to the existing POA&M instead of creating a duplicate.
- **FR-007**: System MUST enforce lifecycle transitions with required context: Delayed requires explanation + revised date; Resume (Delayed → Ongoing) requires a revised completion date; Completed triggers finding validation; Risk Accepted requires linked Deviation record.
- **FR-008**: System MUST maintain a complete audit trail of all POA&M status changes, field edits, and comment additions with actor, timestamp, and details.
- **FR-008a**: System MUST support creating a remediation task directly from a POA&M item, pre-populating the task with POA&M metadata (weakness, control ID, severity, due date, POC).
- **FR-008b**: System MUST support linking an existing remediation task to an existing POA&M item via a searchable picker on either entity.
- **FR-008c**: System MUST propagate status changes bidirectionally between linked POA&M items and remediation tasks, with cascade origin tracking to prevent infinite loops. In the dashboard UI, cascade operations MUST prompt the user for confirmation before applying to the linked entity. Via API/MCP tool calls, cascades MUST auto-apply without prompts, recording full audit trail including cascade source and actor. When cascading Risk Accepted status to a linked remediation task, the system MUST move the task to a "Risk Accepted" column if one exists on the board; otherwise update the task status to Closed with reason "Risk Accepted — linked POA&M deviation".
- **FR-008d**: System MUST propagate shared metadata changes (severity, due date) between linked entities, recording cascade history on both sides.
- **FR-008e**: System MUST display a sync indicator on both the POA&M detail drawer and the remediation task card showing link status, linked entity name, and last sync timestamp.
- **FR-009**: System MUST provide trend charts: open count over time, time-to-close distribution, aging breakdown by severity, and monthly closure rate.
- **FR-010**: System MUST support configuring Jira and ServiceNow integrations with field mapping and webhook-based bidirectional sync. Authentication credentials MUST be stored in Azure Key Vault; the `TicketingIntegration` entity stores a Key Vault secret URI reference, not the credential. The configuration UI collects the credential and writes it to Key Vault on save.
- **FR-011**: System MUST support exporting POA&M data in eMASS Excel, OSCAL JSON, and CSV formats with an option to export filtered or all items.
- **FR-012**: System MUST provide a detail drawer for each POA&M item showing full metadata, linked entities (finding, remediation task, deviation, components, external ticket), milestones, and audit history.
- **FR-013**: System MUST expose all POA&M operations through MCP tools (new and updated) for chat-driven access across dashboard, Teams, and VS Code surfaces. See the MCP Tools section below for the complete tool inventory.
- **FR-014**: System MUST handle concurrent edits with optimistic concurrency and display conflict resolution UI when detected.
- **FR-015**: System MUST support bulk operations: bulk status change, bulk POA&M generation from findings, and bulk sync to external ticketing systems.
- **FR-016**: System MUST refocus the existing Remediation page to manage remediation tasks only — removing POA&M-specific summary cards (Open POA&Ms, Overdue, CAT I Open, Avg Days to Close), severity heatbar, POA&M aging chart, and POA&M-centric table columns (milestones, deviation type). Task pipeline, kanban board, task table, and task-level analytics remain.
- **FR-017**: System MUST add a "Linked POA&M" column to the Remediation task table showing the linked POA&M item's status badge (clickable, navigates to the POA&M Management page detail drawer). Unlinked tasks show "—".
- **FR-018**: System MUST support click-to-open-detail on kanban task cards — clicking (not dragging) a card opens the task detail drawer with full metadata, linked POA&M status, and action buttons. Drag-and-drop behavior is preserved and takes precedence over the click handler.
- **FR-019**: System MUST enforce role-based access on POA&M operations: ISSO, ISSM, AO, and Compliance Officer roles can create, update, and delete POA&M items; Engineer role has read-only POA&M access. Engineers can update linked remediation tasks, and those changes cascade to POA&M items via bidirectional sync (FR-008c/d). The "Add POA&M" button and direct status-change actions MUST be hidden for read-only roles.
- **FR-020**: System MUST provide a dedicated POA&M Management guide at `docs/guides/poam-management.md` covering all page workflows (dashboard, creation, lifecycle, component linkage, remediation sync, scan import, trend analytics, export, ticketing integration), following the existing guide pattern (feature callout, overview, parameter tables, navigation instructions).
- **FR-021**: System MUST update persona-specific documentation (ISSM guide, ISSO getting-started, Engineer guide, AO quick reference, SCA guide) with POA&M-relevant workflows, and update RMF phase guides (Assess, Monitor, Authorize) with POA&M procedures.
- **FR-022**: System MUST update the Agent Tool Catalog and Tool Inventory with all 18 new MCP tools and 3 updated tool entries, including parameter tables, response schemas, RBAC notes, and example invocations. The Data Model reference MUST document new entities (`PoamComponentLink`, `TicketingIntegration`, `PoamTicketSync`) and updated entities.

### Key Entities

- **PoamItem** (existing, extended): Core POA&M record tracking a weakness from discovery through resolution. Links to finding, remediation task, deviation, and system components. Extended with component linkage, external ticket reference fields, and navigation property to `RemediationTask` for bidirectional sync.
- **RemediationTask** (existing, extended): Kanban task for active fix work. Extended with navigation property to `PoamItem` for bidirectional sync. Shared metadata (severity, due date, status) cascades between linked entities.
- **PoamMilestone** (existing): Ordered progress markers within a POA&M item with target dates and completion tracking.
- **PoamComponentLink** (new): Junction entity linking a PoamItem to one or more SystemComponents, enabling asset-level risk tracking.
- **PoamHistoryEntry** (new): Insert-only audit record tracking all POA&M status changes, field edits, milestone updates, and comment additions with actor, timestamp, event type, old/new values, and cascade origin.
- **TicketingIntegration** (new): Configuration entity storing external ticketing system connection parameters (base URL, project/queue mapping), field mappings, and sync state per registered system. Credentials are NOT stored in this entity — authentication tokens are stored in Azure Key Vault and this entity holds a Key Vault secret URI reference.
- **PoamTicketSync** (new): Tracks the sync state between a PoamItem and its external ticket (Jira issue key or ServiceNow incident number, last sync timestamp, sync status).

### MCP Tools

All tools follow the existing `BaseTool` pattern (inherit from `BaseTool`, return JSON `{ status, data, metadata }`). Tool names use the `compliance_<verb>_<noun>` convention. Tools are registered via `ComplianceMcpTools` and exposed through the MCP protocol to all chat surfaces (dashboard, Teams, VS Code).

#### Existing Tools — Updates Required

| Tool | Current State | Required Changes |
|------|---------------|------------------|
| `compliance_create_poam` | Creates POA&M item with milestones | Add `component_ids` parameter (optional string array of SystemComponent IDs to link). Add `remediation_task_id` parameter (optional, to link an existing task bidirectionally). Return `component_links` and `remediation_task` in response. |
| `compliance_list_poam` | Lists POA&M items with basic filtering | Add filters: `component_id`, `overdue_only` (bool), `deviation_type`, `has_remediation_task` (bool), `source` (assessment/scan/manual). Include linked component names, remediation task status, and deviation type in response items. Add `include_metrics` flag to return summary counts (total, overdue, by severity). |
| `compliance_import_nessus` | Imports Nessus/ACAS scans with auto-POA&M creation | Update POA&M auto-creation to also link components when the finding maps to a known inventory item. Return count of POA&M items created vs. deduplicated. |

#### New Tools — POA&M Lifecycle & Updates

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_update_poam` | Update a POA&M item's mutable fields (status, severity, POC, scheduled completion, resources required, comments). Enforces lifecycle transition rules: Delayed requires `delay_reason` + `revised_date`; Completed validates linked finding status; Risk Accepted requires `deviation_id`. Records audit trail entry for every change. | `poam_id` (required), `status`, `cat_severity`, `poc`, `scheduled_completion`, `resources_required`, `delay_reason`, `revised_date`, `deviation_id`, `comment` | FR-007, FR-008 |
| `compliance_get_poam` | Retrieve a single POA&M item with full detail: milestones, linked finding, linked remediation task, linked components, deviation, external ticket, and audit history. | `poam_id` (required), `include_history` (bool, default true) | FR-012 |
| `compliance_close_poam` | Mark a POA&M as Completed with validation. Checks linked finding status and optionally cascades to linked remediation task. Records completion date and actor. | `poam_id` (required), `actual_completion_date` (optional, defaults to now), `cascade_to_task` (bool, default true), `comment` | FR-007, FR-008c |
| `compliance_update_poam_milestone` | Update a milestone within a POA&M item: mark complete, extend target date, update description. Records audit trail. | `poam_id` (required), `milestone_id` or `milestone_sequence` (required), `status`, `completion_date`, `revised_target_date`, `description` | FR-007 |

#### New Tools — Component Linkage

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_link_poam_component` | Link one or more system components to a POA&M item. Creates `PoamComponentLink` junction records. Validates components belong to the same registered system. | `poam_id` (required), `component_ids` (required, string array) | FR-005 |
| `compliance_unlink_poam_component` | Remove component linkage from a POA&M item. Records audit entry. | `poam_id` (required), `component_ids` (required, string array) | FR-005 |
| `compliance_poam_by_component` | List all POA&M items linked to a specific component, with aggregate risk summary (highest CAT severity, open count, overdue count). | `component_id` (required), `status_filter` (optional), `include_risk_summary` (bool, default true) | FR-005, FR-003 |

#### New Tools — Remediation Sync

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_link_poam_task` | Establish bidirectional link between a POA&M item and a remediation task. Sets FKs on both entities, records audit trail on both. Rejects if either entity is already linked to a different counterpart. | `poam_id` (required), `task_id` (required) | FR-008b |
| `compliance_unlink_poam_task` | Remove the bidirectional link between a POA&M item and a remediation task. Clears FKs on both entities, records audit history on both. Neither entity is deleted. | `poam_id` (required), `task_id` (required) | FR-008b |
| `compliance_create_task_from_poam` | Create a new remediation task pre-populated from a POA&M item's metadata (weakness → title, control ID, CAT severity → task severity, scheduled completion → due date, POC → assignee). Establishes bidirectional link. | `poam_id` (required), `board_id` (required), `column_name` (optional, defaults to first column) | FR-008a |

#### New Tools — Trend Analysis & Metrics

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_poam_metrics` | Return POA&M summary metrics for a system or across all systems: total open, overdue, by CAT severity, by status, average days to close, items expiring within 30 days. | `system_id` (optional — omit for cross-system), `date_range_start`, `date_range_end` | FR-002, FR-009 |
| `compliance_poam_trend` | Return time-series POA&M trend data: open count over time, closure rate per period, aging breakdown by severity. Data sourced from compliance snapshots. | `system_id` (optional), `period` (daily/weekly/monthly), `date_range_start`, `date_range_end` | FR-009 |

#### New Tools — Export

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_export_poam` | Export POA&M data in the requested format. Supports eMASS 24-column Excel template, OSCAL POA&M JSON, and CSV. Respects active filters. | `system_id` (required), `format` (required: `emass_excel`, `oscal_json`, `csv`), `status_filter`, `severity_filter`, `include_all` (bool, ignores filters) | FR-011 |

#### New Tools — Bulk Operations

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_bulk_update_poam` | Apply a status change or field update to multiple POA&M items at once. Enforces the same lifecycle rules as single updates. Returns per-item success/failure results. | `poam_ids` (required, string array), `status`, `cat_severity`, `poc`, `scheduled_completion`, `comment` | FR-015 |
| `compliance_bulk_create_poam_from_findings` | Auto-generate POA&M items from a list of finding IDs with duplicate detection. Optionally links to components and remediation tasks. Returns created count and skipped duplicates. | `system_id` (required), `finding_ids` (required, string array), `component_ids` (optional), `link_remediation_tasks` (bool, default false) | FR-006, FR-015 |

#### New Tools — External Ticketing

| Tool | Description | Key Parameters | Supports (FR) |
|------|-------------|----------------|----------------|
| `compliance_configure_ticketing` | Configure a Jira or ServiceNow integration for a system. Stores connection parameters, field mappings, and sync preferences. Validates connectivity on save. | `system_id` (required), `provider` (required: `jira`, `servicenow`), `base_url`, `project_key` or `table_name`, `auth_token`, `field_mapping` (JSON), `sync_enabled` (bool) | FR-010 |
| `compliance_sync_poam_ticket` | Sync a single POA&M item to/from its external ticketing system. Creates the external ticket if it doesn't exist, or reconciles state if it does. Returns sync result with conflict details if any. | `poam_id` (required), `direction` (optional: `push`, `pull`, `bidirectional` — default `bidirectional`) | FR-010 |
| `compliance_bulk_sync_tickets` | Sync all unsynced or changed POA&M items for a system to the configured external ticketing system in a single batch. Returns per-item sync results. | `system_id` (required), `direction` (optional, default `bidirectional`) | FR-010, FR-015 |

#### Tool Summary

| Category | New | Updated | Total |
|----------|-----|---------|-------|
| Lifecycle & Updates | 4 | 0 | 4 |
| Component Linkage | 3 | 0 | 3 |
| Remediation Sync | 3 | 0 | 3 |
| Trend Analysis & Metrics | 2 | 0 | 2 |
| Export | 1 | 0 | 1 |
| Bulk Operations | 2 | 0 | 2 |
| External Ticketing | 3 | 0 | 3 |
| Existing Updates | 0 | 3 | 3 |
| **Total** | **18** | **3** | **21** |

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: ISSMs can view the full POA&M posture for a system within 3 seconds of page load, without switching to a separate tool or spreadsheet.
- **SC-002**: Auto-generating POA&M items from a scan import of 100+ findings completes within 10 seconds with zero duplicate entries.
- **SC-002a**: 100% of remediation tasks created from assessment results have a linked POA&M item from the moment of creation — no manual linking step required.
- **SC-003**: 90% of POA&M items have at least one linked component within 30 days of feature adoption, enabling asset-level risk reporting.
- **SC-004**: Average time from scan import to POA&M item creation drops by 80% compared to manual entry workflows.
- **SC-005**: POA&M trend reports replace manually compiled spreadsheets for authorization decision packages, measured by at least 3 AO-level exports per month per active organization.
- **SC-006**: Ticketing integration sync operates with no more than 15-minute latency and fewer than 1% sync failures per week.
- **SC-007**: All POA&M lifecycle transitions are traceable — every status change has a recorded actor, timestamp, and context (100% audit coverage).
- **SC-008**: Users can complete the end-to-end workflow (scan import → POA&M creation → component linkage → status tracking → export) without leaving the POA&M Management page.
- **SC-009**: All 18 new MCP tools and 3 updated tools are registered and callable from dashboard chat, Teams, and VS Code — enabling the full POA&M lifecycle (create, update, close, link, export, trend) via natural language.
- **SC-010**: The Remediation page shows only task-management UI — no POA&M summary duplication. Users can reach any linked POA&M from the task table or kanban detail drawer within one click.
- **SC-011**: All 7 documentation areas (POA&M guide, persona guides, tool catalog, tool inventory, data model, RMF phases, supporting references) are published and accessible from the MkDocs navigation within one release cycle of the corresponding feature shipping.
