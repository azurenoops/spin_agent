# Feature Specification: Dashboard User Documentation

**Feature Branch**: `032-dashboard-documentation`  
**Created**: 2025-07-14  
**Status**: Draft  
**Input**: User description: "create documentation for the dashboard that helps users understand all features and components and allows the user to complete any task in the dashboard"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Getting Started with the Dashboard (Priority: P1)

A new user opens the ATO Copilot Dashboard for the first time. They need to understand what the dashboard does, how to navigate between pages, and where to find information about their systems. The documentation provides a "Getting Started" guide that orients the user to the layout — header navigation (Portfolio, Capabilities), the collapsible side panel (To Do), and the main content area. After reading, the user can navigate to any page and identify what each area shows.

**Why this priority**: First impressions determine adoption. If a new user cannot orient themselves within the first 5 minutes, they will disengage.

**Independent Test**: A user who has never used the dashboard can navigate to every page and describe the purpose of each section within 5 minutes after reading the Getting Started guide.

**Acceptance Scenarios**:

1. **Given** a new user reading the Getting Started guide, **When** they open the dashboard, **Then** they can identify the header navigation, main content area, side panel, and breadcrumb navigation.
2. **Given** a new user, **When** they follow the guide's navigation instructions, **Then** they can successfully navigate to Portfolio, System Detail, Capabilities, Gap Analysis, Component Inventory, and Implementation Roadmap pages.
3. **Given** a new user, **When** they read the glossary section, **Then** they understand key compliance terms (RMF, ATO, POA&M, CAT I/II/III, NIST 800-53).

---

### User Story 2 — Understanding the Portfolio Dashboard (Priority: P1)

A compliance manager opens the Portfolio page and needs to understand how to sort systems, filter by impact level or RMF phase, interpret compliance score trends, ATO countdown colors, and finding counts. The documentation explains every column, filter option, and interaction pattern (clicking a row to drill into a system).

**Why this priority**: The Portfolio is the landing page and the primary entry point — every user sees it first.

**Independent Test**: A user can sort the portfolio by any column, apply filters, and correctly interpret what each metric means.

**Acceptance Scenarios**:

1. **Given** the Portfolio documentation, **When** a user reads the column descriptions, **Then** they understand each column: System Name, Impact Level, RMF Phase, Compliance Score, ATO Countdown, POA&Ms.
2. **Given** the Portfolio documentation, **When** a user follows the sorting instructions, **Then** they can sort by any column ascending or descending.
3. **Given** the Portfolio documentation, **When** a user reads the filter section, **Then** they can filter by Impact Level (Low/Moderate/High) and by RMF Phase (7 phases).
4. **Given** the Portfolio documentation, **When** a user reads the ATO severity color guide, **Then** they can correctly interpret green (>90 days), yellow (30–90 days), red (<30 days), and expired statuses.

---

### User Story 3 — Working with System Detail (Priority: P1)

An ISSO opens the System Detail page for a specific system and needs documentation to understand the RMF Phase Progress stepper, the four key metric cards (Compliance Score, ATO Status, POA&Ms, Narrative Coverage), the compliance heatmap with drill-down, the compliance trends chart, the findings severity display, the activity feed, and the To Do panel. The documentation walks through each section and explains what actions can be taken. Each section also has a contextual help icon (?) that displays a brief overview of what the section shows and how to interact with it.

**Why this priority**: System Detail is the most content-rich page where users spend the majority of their time performing compliance work.

**Independent Test**: A user can explain each section of the System Detail page, drill into a heatmap family, adjust trend chart granularity and date range, and understand what each To Do item means.

**Acceptance Scenarios**:

1. **Given** the System Detail documentation, **When** a user reads the RMF Progress section, **Then** they understand completed (green), current (blue), and upcoming (gray) phases with completion percentages.
2. **Given** the documentation, **When** a user reads the Key Metrics section, **Then** they can interpret compliance score deltas, ATO countdown severity, POA&M counts (total vs overdue), and narrative coverage percentage.
3. **Given** the documentation, **When** a user reads the Heatmap section, **Then** they can click a control family cell, view individual controls, and understand Satisfied/OtherThanSatisfied/NotAssessed statuses.
4. **Given** the documentation, **When** a user reads the Trends section, **Then** they can change granularity (Daily/Weekly/Monthly/Quarterly), adjust date ranges, and spot significant compliance declines (red indicators).
5. **Given** the documentation, **When** a user reads the To Do panel section, **Then** they understand how to click a to-do item and choose between "Open in Dashboard" or "Ask in Teams/VS Code."

---

### User Story 4 — Managing Security Capabilities (Priority: P2)

A security engineer needs to create, edit, and manage security capabilities in the Capability Library. The documentation explains the search and filter features, how to create a new capability with all required fields (name, provider, category, status), how to edit or delete existing capabilities, and how to manage control-to-capability mappings (adding control IDs with roles: Primary/Supporting/Shared).

**Why this priority**: Capabilities are the bridge between NIST controls and implementation — essential for closing compliance gaps.

**Independent Test**: A user can create a new capability, add control mappings to it, edit it, and delete it following only the documentation.

**Acceptance Scenarios**:

1. **Given** the Capabilities documentation, **When** a user follows the "Create Capability" guide, **Then** they can fill in name, provider, category, status, and description to create a new capability.
2. **Given** the documentation, **When** a user follows the "Edit Capability" guide, **Then** they can modify any field and save changes.
3. **Given** the documentation, **When** a user follows the "Manage Mappings" guide, **Then** they can add a control mapping with a NIST control ID and a role (Primary/Supporting/Shared).
4. **Given** the documentation, **When** a user reads the filtering instructions, **Then** they can search by name, filter by NIST category, and filter by implementation status.

---

### User Story 5 — Managing Components (Priority: P2)

A system owner needs to inventory the People, Places, and Things components of their system. The documentation explains the three collapsible sections, how to create a new component (name, type, subtype, status, owner), how to link a component to security capabilities, and how to edit or remove components.

**Why this priority**: Component inventory is required for authorization boundary documentation, which feeds directly into the SSP.

**Independent Test**: A user can add a Person, Place, and Thing component, link each to a capability, and delete one — all following the documentation.

**Acceptance Scenarios**:

1. **Given** the Components documentation, **When** a user follows the "Add Component" guide, **Then** they can create components of type Person, Place, or Thing with required fields.
2. **Given** the documentation, **When** a user reads the "Link Capabilities" section, **Then** they can associate a component with one or more security capabilities.
3. **Given** the documentation, **When** a user reads the collapsible sections explanation, **Then** they understand how to expand/collapse each category and view summary counts.

---

### User Story 6 — Using Gap Analysis (Priority: P2)

An ISSM needs to identify which NIST control families have compliance gaps and which specific controls within those families are unmapped. The documentation explains the summary metrics row (Total Controls, Covered, Gaps, Coverage %), the per-family coverage matrix with percentage bars and color coding, and how to expand a family row to see individual unmapped controls.

**Why this priority**: Gap analysis directly drives remediation priorities — users need to identify and close gaps before authorization.

**Independent Test**: A user can identify the three lowest-coverage control families and find specific unmapped controls within each.

**Acceptance Scenarios**:

1. **Given** the Gap Analysis documentation, **When** a user reads the Summary Metrics section, **Then** they understand total controls, covered controls, gap count, and overall coverage percentage.
2. **Given** the documentation, **When** a user reads the Coverage Matrix section, **Then** they can interpret color-coded rows (green ≥80%, yellow 50–79%, red <50%) and identify families needing attention.
3. **Given** the documentation, **When** a user follows the "Drill Down" instructions, **Then** they can expand a family row to see individual unmapped controls.

---

### User Story 7 — Viewing Implementation Roadmap (Priority: P3)

A project manager needs to understand the multi-phase remediation roadmap, review the timeline visualization, and track risk reduction over time. The documentation explains the summary metrics (Total Gaps, Effort Days, Risk Reduction %, Timeline in Weeks), the Gantt-style timeline bars, phase status badges (Complete/InProgress/NotStarted), and the risk reduction curve chart showing projected vs actual risk decline.

**Why this priority**: The roadmap is a planning tool used less frequently than day-to-day compliance views.

**Independent Test**: A user can read the roadmap page, identify which phase is currently active, and interpret the risk reduction curve.

**Acceptance Scenarios**:

1. **Given** the Roadmap documentation, **When** a user reads the Timeline section, **Then** they understand how phases are ordered, their estimated effort, and their completion status.
2. **Given** the documentation, **When** a user reads the Risk Reduction Curve section, **Then** they can compare projected vs actual risk reduction progress.
3. **Given** the documentation, **When** a user reads the Phase Detail section, **Then** they understand how individual roadmap items (controls) are grouped by phase with status indicators.

---

### User Story 8 — Using the To Do Panel to Take Action (Priority: P2)

A user opens the System Detail page and sees the To Do side panel showing phase-aware action items. They need documentation explaining what each to-do category means (phase-action, finding, POA&M, narrative, authorization), how the system determines what items to show based on the current RMF phase, and how to use the action dialog to either navigate to the relevant dashboard page or copy a natural language prompt for Teams / VS Code.

**Why this priority**: The To Do panel is the primary driver of user actions — it tells users what to do next.

**Independent Test**: A user can click a to-do item, understand the action dialog options, and successfully copy a prompt or navigate to a dashboard page.

**Acceptance Scenarios**:

1. **Given** the To Do documentation, **When** a user reads the phase-action explanation, **Then** they understand that to-do items change based on which RMF phase the system is currently in.
2. **Given** the documentation, **When** a user follows the "Take Action" guide, **Then** they can click a to-do item, choose "Open in Dashboard" to navigate to the relevant page, or choose "Ask in Teams/VS Code" to copy an `@ato` prompt.
3. **Given** the documentation, **When** a user reads the category descriptions, **Then** they understand the difference between phase-action, finding, POA&M, narrative, and authorization items.

---

### Edge Cases

- What happens when a user accesses a system that has no data yet (no assessments, no baseline selected)? Tooltips include empty state guidance directing users to the next step.
- How does documentation handle the collapsible side panel toggle on smaller screen sizes?
- What if screenshots become outdated after a UI update — how are they refreshed?
- How does the documentation handle features visible only to certain personas?
- What happens when the API is unreachable and dashboard components show loading or error states?
- How does the help slide-out panel interact with the To Do side panel — can both be open simultaneously, or does one replace the other?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Documentation MUST include a Getting Started guide covering dashboard layout, navigation, and key terminology.
- **FR-002**: Documentation MUST include a dedicated page for each of the six dashboard pages: Portfolio, System Detail, Capabilities, Components, Gap Analysis, and Implementation Roadmap.
- **FR-003**: Each page documentation MUST describe all visible sections, data displayed, and available user interactions.
- **FR-004**: Documentation MUST include annotated screenshots or diagrams for each page showing labeled UI regions.
- **FR-005**: Documentation MUST include step-by-step task guides for all CRUD operations: creating, editing, and deleting capabilities and components.
- **FR-006**: Documentation MUST include a reference section explaining color codes, severity levels, status values, and RMF phase meanings.
- **FR-007**: Documentation MUST include a guide for the To Do panel explaining action categories, the action dialog, and both action paths (dashboard navigation and Teams/VS Code prompt copy).
- **FR-008**: Documentation MUST include a glossary of compliance terms (RMF, ATO, POA&M, FIPS 199, NIST 800-53, CAT I/II/III, SSP, SAR, ConMon).
- **FR-009**: Documentation MUST explain the heatmap drill-down workflow: clicking a family cell, viewing individual controls, and understanding compliance statuses (Satisfied, OtherThanSatisfied, NotAssessed).
- **FR-010**: Documentation MUST explain the compliance trends chart including granularity options, date range selection, significant decline indicators, and the 80% target reference line.
- **FR-011**: Documentation MUST explain how to interpret the Gap Analysis coverage matrix including color coding thresholds, family expansion, and identifying critical gaps.
- **FR-012**: Documentation MUST explain the Implementation Roadmap including timeline bars, phase status badges, risk reduction curve, and summary metrics.
- **FR-013**: Documentation MUST be structured as a single comprehensive guide page covering all dashboard pages and features.
- **FR-014**: Documentation MUST be accessible from the help icon (?) in the dashboard header bar via a slide-out panel from the right side.
- **FR-015**: Documentation MUST follow the existing documentation style and formatting conventions used in other guides.
- **FR-016**: Documentation MUST include cross-references to related persona guides (ISSO, ISSM, SCA, AO, Engineer) where applicable.
- **FR-017**: The dashboard MUST display contextual help icons (?) next to the following System Detail sections: ToDo, RMF Phase Progress, Compliance Score, ATO Status, POA&Ms, Narrative Coverage, Findings, Compliance Trends, and Recent Activity.
- **FR-018**: Each contextual help icon MUST display a popover with a short paragraph (2–3 sentences) explaining what the section shows, how to read it, and one key interaction hint.
- **FR-019**: Documentation MUST also be accessible through the existing MkDocs documentation site under the `docs/guides/` directory.
- **FR-020**: Each contextual help popover MUST include empty state guidance explaining why the section may be empty and what action the user should take to populate it.

### Key Entities

- **Page Guide**: A documentation section for one dashboard page, containing overview, annotated screenshot, sections breakdown, interactions, and task walkthroughs.
- **Task Guide**: A step-by-step procedure for completing a specific action (e.g., "Create a Security Capability"), with numbered steps and expected outcomes.
- **Reference Table**: A lookup table explaining values, color codes, statuses, or terminology used across the dashboard.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with no prior dashboard experience can navigate to every page and identify the purpose of each section within 5 minutes of reading the Getting Started guide.
- **SC-002**: A user can complete any CRUD operation (create, edit, delete a capability or component) on the first attempt using only the documentation.
- **SC-003**: 100% of dashboard pages (6 pages) have dedicated documentation sections with annotated visuals.
- **SC-004**: All color codes, severity levels, and status values referenced in the dashboard are explained in the documentation reference section.
- **SC-005**: The documentation covers all 8 user stories defined in this specification.
- **SC-006**: A user can understand and use the To Do panel action dialog (both options) within 2 minutes of reading the To Do guide.
- **SC-007**: The documentation is published and accessible through the MkDocs site navigation under a "Dashboard Guide" section.

## Clarifications

### Session 2026-03-15

- Q: Should the dashboard documentation be structured as a single comprehensive guide page or multiple separate pages? → A: Single comprehensive page (Option A), attached to the help icon in the header bar.
- Q: When a user clicks the header help icon, how should the documentation be presented? → A: Slide-out panel from the right side (similar to the To Do panel).
- Q: How detailed should the contextual help tooltips next to System Detail sections be? → A: Short paragraph (2–3 sentences) covering what the section shows, how to read it, and one key interaction hint.
- Q: Should tooltips explain what to do when a section has no data (empty state)? → A: Yes — each tooltip includes a sentence about what to do when the section is empty.

## Assumptions

- The dashboard UI is stable and screenshots taken during documentation will remain valid for at least one release cycle.
- The MkDocs documentation site infrastructure is already configured and new pages can be added by creating markdown files in `docs/guides/`.
- Users have basic familiarity with web browsers and standard UI patterns (clicking, scrolling, form inputs) but no prior knowledge of compliance frameworks.
- The documentation will be written in English and does not require localization at this time.
- Screenshots will be captured from the live dashboard running with the Eagle Eye test system data.
- The `mkdocs.yml` navigation configuration will be updated to include the new documentation pages.
