# Feature Specification: Visual Compliance Dashboard & Risk Solutions Library

**Feature Branch**: `030-compliance-dashboard`
**Created**: 2026-03-14
**Status**: Draft
**Input**: User description: "P0 enhancements from Paramify competitive analysis — Visual Compliance Dashboard (React web app) and Risk Solutions / Security Capabilities Library"

## Clarifications

### Session 2026-03-14

- Q: Are Security Capabilities scoped per-organization or per-system? → A: Organization-wide — capabilities exist globally and can be mapped to controls on any system.
- Q: When a user manually edits an auto-generated narrative, what happens on the next capability update? → A: Protect manual edits — manually modified narratives are flagged as "customized" and skipped during auto-update; the user is notified of the upstream change and can choose to re-sync or keep their version.
- Q: How is RMF phase completion percentage measured? → A: Narrative coverage — completion % equals the proportion of controls applicable to that phase that have populated implementation narratives (e.g., Implement phase = narratives written / total baseline controls).
- Q: How should the frontend obtain updated dashboard data, and where does the dashboard live? → A: The dashboard is its own standalone project (separate from the Chat App). Use polling (15-30 second interval) for data refresh to keep the architecture simple and self-contained.
- Q: What category taxonomy should Security Capabilities use? → A: NIST SP 800-53 control families — categories mirror the 19 control families (Access Control, Audit & Accountability, etc.). A capability belongs to one primary family category.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Portfolio Dashboard Overview (Priority: P1)

An ISSM or AO opens the ATO Copilot web application and immediately sees a portfolio-level dashboard showing all registered systems at a glance. Each system displays its impact level, current RMF phase, overall compliance score, ATO expiration countdown, and open POA&M count. The user can sort and filter by any column. Clicking a system row drills into the single-system detail view.

**Why this priority**: This is the foundational visual layer. Without a portfolio overview, users must query each system individually via chat commands. A single-pane view transforms usability and is the #1 gap vs. Paramify.

**Independent Test**: Can be fully tested by registering 3+ systems with varying RMF phases, running assessments on at least one, and verifying the dashboard renders correct summary metrics for each. Delivers immediate value as a read-only status board.

**Acceptance Scenarios**:

1. **Given** 3 registered systems at different RMF phases, **When** the user navigates to the portfolio dashboard, **Then** all 3 systems appear with correct IL, RMF phase, compliance score, ATO expiration (or "--" if not authorized), and POA&M counts.
2. **Given** a portfolio with 10+ systems, **When** the user sorts by compliance score descending, **Then** systems are reordered from highest to lowest score.
3. **Given** a portfolio dashboard is open, **When** a compliance assessment completes for a system in the background, **Then** the dashboard updates the score within 30 seconds (via polling or push) without requiring a full page reload.
4. **Given** a system has an ATO expiring in fewer than 90 days, **When** the portfolio dashboard renders, **Then** the expiration cell displays a graduated visual severity indicator (green > 90d, yellow 30-90d, red < 30d).

---

### User Story 2 — Single-System Compliance Roadmap (Priority: P1)

An ISSM clicks into a specific system from the portfolio view and sees a detailed compliance roadmap. The view includes: (a) an RMF phase progress indicator showing all 7 phases with the current phase highlighted and completion percentage per phase, (b) a control family heatmap showing compliance percentage across all 19 NIST SP 800-53 control families, (c) key metric cards (compliance score with trend, open POA&Ms with overdue count, ATO countdown, open findings by CAT severity), and (d) a recent activity feed showing the last 10 compliance events (assessments, narrative updates, scan imports, authorization decisions).

**Why this priority**: This is the "living dashboard" equivalent from Paramify — the central place where an ISSM understands system health at a glance. Tied P1 with portfolio because both form the core dashboard experience.

**Independent Test**: Can be fully tested by registering one system, advancing it through at least 3 RMF phases, writing narratives for several control families, running an assessment, and importing a scan. Dashboard should reflect all of this accurately.

**Acceptance Scenarios**:

1. **Given** a system at RMF Step 4 (Implement) with 60% of SC-family narratives written, **When** the user opens the system dashboard, **Then** the RMF phase indicator highlights "Implement" as current, shows "Prepare", "Categorize", and "Select" as complete (100%), and shows an overall progress bar reflecting narrative completion percentage.
2. **Given** a system with completed assessments across AC, AU, SC, CM families, **When** the control family heatmap renders, **Then** each family cell is color-coded by compliance percentage (green >= 80%, yellow 50-79%, red < 50%, gray = not assessed).
3. **Given** a system with 5 CAT I, 12 CAT II, and 8 CAT III open findings, **When** the findings metric card renders, **Then** it shows the total count (25) with a breakdown bar by severity.
4. **Given** a user imports a CKL scan for a system, **When** they return to the system dashboard, **Then** the recent activity feed shows the import event with timestamp, file name, and finding count.

---

### User Story 3 — Security Capabilities Library (Risk Solutions) (Priority: P1)

A compliance team creates reusable, organization-wide Security Capabilities (inspired by Paramify's "Risk Solutions") that represent the organization's security measures (e.g., "Multi-Factor Authentication via Microsoft Entra ID", "Encryption at Rest via Azure Key Vault"). Capabilities are shared across all registered systems — a single "MFA" capability can be mapped to controls on every system in the portfolio. When the team updates a capability's description or provider, all mapped control narratives across all systems are automatically updated to reflect the change.

**Why this priority**: This eliminates the #1 efficiency gap vs. Paramify — the "write once, apply everywhere" model. Without this, updating a single security technology (e.g., switching from Duo to Okta for MFA) requires manually editing 81+ individual control narratives.

**Independent Test**: Can be fully tested by creating a "Multi-Factor Authentication" capability, mapping it to 5 controls (e.g., AC-2, AC-7, IA-2, IA-5, IA-8), generating narratives from the capability, then updating the capability provider and verifying all 5 narratives auto-update.

**Acceptance Scenarios**:

1. **Given** a user creates a new Security Capability named "MFA" with provider "Microsoft Entra ID" and description text, **When** they map it to controls AC-2, AC-7, IA-2, IA-5, and IA-8, **Then** each control's implementation narrative is populated with the capability's description contextualized to that control's requirements.
2. **Given** a capability "MFA" is mapped to 5 controls with generated narratives, **When** the user updates the capability provider from "Duo" to "Okta", **Then** all 5 control narratives are regenerated with "Okta" replacing "Duo" and a change audit entry is created.
3. **Given** a user views the Security Capabilities Library, **When** they search for "encryption", **Then** all capabilities with "encryption" in name, description, or provider are shown, each displaying its mapped control count and implementation status.
4. **Given** a capability exists with 30 mapped controls, **When** the user clicks the capability to view details, **Then** they see a list of all mapped controls grouped by family, with each control showing its current narrative status (Populated/Empty/Customized).

---

### User Story 4 — Capability-to-Control Mapping Management (Priority: P2)

A compliance analyst manages the many-to-many mappings between Security Capabilities and NIST controls. The system provides a visual mapping interface showing which capabilities satisfy which controls, identifies unmapped controls (gaps), and prevents conflicting mappings (e.g., two capabilities claiming to be the sole provider for the same control in a "primary" role).

**Why this priority**: Builds on US3 to provide the gap visibility that makes Paramify's gap assessment powerful. Essential for understanding coverage but the core capability CRUD (US3) can be tested independently first.

**Independent Test**: Can be tested by creating 5 capabilities, mapping them to various controls, and verifying the gap report correctly identifies unmapped controls from the selected baseline.

**Acceptance Scenarios**:

1. **Given** a system with a Moderate baseline (325 controls) and 3 capabilities mapped to 150 unique controls, **When** the user requests a capability coverage report, **Then** the report shows 150 controls covered, 175 controls unmapped, organized by control family with coverage percentages.
2. **Given** capability "MFA" is mapped to IA-2 as primary, **When** another capability "SSO Gateway" is also mapped to IA-2 as primary, **Then** the system warns about the duplicate primary assignment and asks the user to designate one as primary and the other as supporting.
3. **Given** a Moderate baseline, **When** the user views the capability gap matrix, **Then** each control family row shows: total controls, covered controls, gap count, and coverage percentage, with families below 50% coverage highlighted.

---

### User Story 5 — System Component Inventory (People/Places/Things) (Priority: P2)

An ISSM builds a structured inventory of the system's components organized by type: People (roles and personnel), Places (data centers, cloud regions, facilities), and Things (tools, applications, cloud services). These components link to Security Capabilities and auto-populate the SSP Appendix A (System Components table). When a component is added or removed, its linked capability descriptions and SSP sections update accordingly.

**Why this priority**: Paramify's "intake session" revolves entirely around People/Places/Things. This bridges the gap but requires US3 (capabilities) to be in place first for linking.

**Independent Test**: Can be tested by adding people (ISSM role), places (Azure Government East US), and things (Microsoft Defender for Cloud), linking them to capabilities, then generating an SSP and verifying Appendix A populates correctly.

**Acceptance Scenarios**:

1. **Given** a user adds 3 People (ISSM, ISSO, SCA), 2 Places (Azure Gov East, Azure Gov West), and 5 Things (Entra ID, Defender, Key Vault, Log Analytics, Sentinel), **When** they view the component inventory, **Then** components are organized into collapsible sections by type with total counts per section.
2. **Given** components are linked to Security Capabilities, **When** the user generates an SSP, **Then** the SSP Appendix A is populated with a component table listing each item with type, description, linked capabilities, and owner.
3. **Given** a Thing "Duo MFA" is removed from inventory, **When** the system processes the removal, **Then** any Security Capabilities referencing Duo are flagged for review, and the user is prompted to update or reassign affected capabilities.

---

### User Story 6 — Compliance Trend Analytics (Priority: P2)

An ISSM or AO views compliance score trends over time for a system or across the portfolio. The system captures periodic snapshots of compliance scores, finding counts, and POA&M status, and presents them as time-series visualizations. Users can view trends at daily, weekly, monthly, or quarterly granularity over configurable date ranges.

**Why this priority**: Paramify highlights "Actionable Trend Insights" as a key differentiator. Trend data answers "are we getting better or worse?" — critical for ConMon reporting and leadership briefings.

**Independent Test**: Can be tested by running multiple assessments over several days for a system, then viewing the trend chart to verify data points render accurately with the correct date and score values.

**Acceptance Scenarios**:

1. **Given** a system with 5 assessments over the past 90 days, **When** the user views the compliance trend chart, **Then** a line graph shows score over time with data points at each assessment date.
2. **Given** a portfolio with 3 systems, **When** the user views the portfolio trend view at monthly granularity, **Then** each system is rendered as a separate line on the same chart, with a legend identifying each system.
3. **Given** a system's score dropped from 85% to 72% in the last 30 days, **When** the trend chart renders, **Then** the declining segment is highlighted in red/orange to draw attention to the degradation.

---

### User Story 7 — Dashboard API Endpoints (Priority: P1)

The backend exposes RESTful API endpoints that aggregate compliance data into dashboard-ready payloads. These endpoints serve the frontend dashboard components and are also available for external integrations. Endpoints include portfolio summary, single-system detail, capability library, component inventory, coverage gaps, and trend data.

**Why this priority**: The frontend dashboard (US1, US2) depends entirely on these API endpoints. They must exist before any UI work begins.

**Independent Test**: Can be fully tested via HTTP requests (no UI required) — call each endpoint and validate JSON response payloads against expected schemas. This is the pure backend work that unblocks all frontend stories.

**Acceptance Scenarios**:

1. **Given** 3 registered systems, **When** a GET request is made to the portfolio summary endpoint, **Then** a JSON response returns an array of system summaries with fields: systemId, name, impactLevel, currentRmfStep, complianceScore, atoExpirationDate, openPoamCount, overduePoamCount.
2. **Given** a system with assessment data, **When** a GET request is made to the system detail endpoint, **Then** the response includes: rmfPhaseProgress (array of 7 phases with completion %), controlFamilyHeatmap (19 families with compliance %), keyMetrics (score, delta, poam counts, finding counts by severity), and recentActivity (last 10 events).
3. **Given** 10 Security Capabilities in the library, **When** a GET request is made to the capabilities list endpoint with search query "auth", **Then** only capabilities matching "auth" in name/description/provider are returned, each with mappedControlCount and status.
4. **Given** a system with partial capability coverage, **When** a GET request is made to the gap analysis endpoint, **Then** the response includes: totalBaselineControls, coveredControls, gapCount, and a per-family breakdown with coveragePercentage.

---

### Edge Cases

- What happens when a system has zero assessments? Dashboard shows "No assessments yet" with a call-to-action to run the first assessment.
- How does the portfolio dashboard handle a system with a revoked or expired ATO? The ATO status cell shows "EXPIRED" or "REVOKED" in red with the date, and the system sorts to the top when sorted by expiration.
- What happens when a Security Capability is deleted that has mapped controls? The system prompts for confirmation, warns about affected control count, and on deletion marks affected narratives as "capability removed — review required."
- How does the heatmap handle control families with zero controls in the selected baseline? Families not in the baseline are hidden from the heatmap (e.g., PT family may not appear for Low baseline).
- What happens when two capabilities have overlapping control mappings? The ControlImplementation.SecurityCapabilityId tracks the Primary capability only. If a control has multiple mapped capabilities, the Primary capability's narrative is stored in ControlImplementation; Supporting/Shared capabilities are tracked via CapabilityControlMapping but do not overwrite the primary narrative. The mapping panel shows all capabilities with their roles.
- How does the system handle a component inventory with 500+ items? The inventory view supports pagination (50 items per page), search, and type filtering.
- What happens if no Security Capabilities exist? The library shows an empty state with a "Create your first Security Capability" prompt and a link to documentation.
- How does the dashboard behave on mobile or narrow screens? Dashboard components reflow into a single-column layout with collapsible sections; heatmap switches to a simplified list view.

## Requirements *(mandatory)*

### Functional Requirements

**Portfolio Dashboard:**

- **FR-001**: System MUST provide a portfolio view listing all registered systems with: system name, impact level, current RMF phase, compliance score, ATO expiration date, open POA&M count, and overdue POA&M count.
- **FR-002**: System MUST support sorting the portfolio list by the following columns in ascending or descending order: name, impactLevel, rmfPhase, complianceScore, atoExpiration, openPoamCount.
- **FR-003**: System MUST support filtering the portfolio list by impact level and RMF phase.
- **FR-004**: System MUST display ATO expiration countdown with graduated severity indicators (green > 90 days, yellow 30-90 days, red < 30 days, expired = past expiration date, none = no ATO decision). The frontend renders "expired" using a dark/black visual style.
- **FR-005**: System MUST refresh dashboard data automatically via polling (15-30 second interval) without requiring a full page reload, with no more than 30-second staleness.

**Single-System Dashboard:**

- **FR-006**: System MUST display an RMF phase progress indicator showing all 7 RMF phases with the current phase highlighted and a completion metric per phase. Phase completion percentage is defined as the proportion of baseline controls applicable to that phase that have populated implementation narratives (narratives written / total baseline controls).
- **FR-007**: System MUST display a control family heatmap covering all 19 NIST SP 800-53 control families with color-coded compliance percentages for the system's selected baseline.
- **FR-008**: System MUST display key metric cards: compliance score (with trend indicator vs. prior period), total open POA&Ms (with overdue count), ATO days remaining, and open findings by CAT severity (I/II/III).
- **FR-009**: System MUST display a recent activity feed showing the last 10 compliance events with event type, timestamp, actor, and summary.
- **FR-010**: System MUST allow drill-down from the control family heatmap to a list of individual controls within the selected family, served by a dedicated API endpoint (`GET /api/dashboard/systems/{systemId}/heatmap/{familyCode}/controls`) returning control IDs, titles, and compliance status.
- **FR-010a**: The control family drill-down dialog MUST display summary statistics (Satisfied, Failing, Not Assessed counts) as clickable filter cards that narrow the visible control list by status.
- **FR-010b**: The control family drill-down MUST display CAT severity (I/II/III) for each failing control, sourced from `ControlEffectivenessRecord.CatSeverity`, with color-coded badges (CAT I = red, CAT II = amber, CAT III = blue).
- **FR-010c**: The control family drill-down MUST display the POA&M status for each control that has an associated POA&M item, sourced by joining `PoamItems` on `SecurityControlNumber`, with color-coded status badges.
- **FR-010d**: The control family drill-down MUST include an action banner when failing controls exist, with navigation links to the Remediation and POA&M pages.
- **FR-010e**: The control family drill-down MUST include footer quick links to "Edit Narratives" and "Run Assessment" pages for the current system.

**Navigation:**

- **FR-036**: The system-level sidebar navigation MUST organize navigation items into logical groups: System Profile (Overview, Components, Boundaries, Capabilities), Compliance Posture (Narratives, Legal & Regulatory, Gap Analysis), Assessment & Remediation (Assessments, Remediation, POA&M, Evidence, Deviations), and Planning & Delivery (Implementation Roadmap, Documents).
- **FR-037**: When the sidebar is expanded, group labels MUST be displayed above their items. When the sidebar is collapsed, a thin divider MUST separate groups visually.

**Security Capabilities Library:**

- **FR-011**: System MUST allow users to create Security Capabilities with: name, provider/vendor, category (one of the 19 NIST SP 800-53 control families: AC, AT, AU, CA, CM, CP, IA, IR, MA, MP, PE, PL, PM, PS, PT, RA, SA, SC, SI), description (rich text), implementation status (Planned/In Progress/Implemented/Deprecated), and owner.
- **FR-012**: System MUST allow mapping a Security Capability to one or more NIST 800-53 controls with a role designation (Primary/Supporting/Shared).
- **FR-013**: System MUST auto-generate control implementation narratives from mapped Security Capability descriptions, contextualized to each control's requirements.
- **FR-014**: When a Security Capability's description or provider is updated, the system MUST propagate changes to all mapped control narratives that have not been manually customized, and create an audit entry recording the change. Narratives that were manually edited after initial generation MUST be flagged as "customized" and excluded from auto-update; the system MUST notify the user that an upstream capability change is available and offer a re-sync option.
- **FR-015**: System MUST provide a searchable library view with filtering by NIST control family category, implementation status, and provider.
- **FR-016**: System MUST display capability coverage statistics: total mapped controls, coverage percentage against the selected baseline, and coverage breakdown by control family.

**Capability-Control Mapping:**

- **FR-017**: System MUST identify unmapped controls (gaps) by comparing mapped capabilities against the system's selected baseline.
- **FR-018**: System MUST generate a gap analysis report showing: total baseline controls, covered count, gap count, and per-family coverage percentages.
- **FR-019**: System MUST warn when two capabilities claim primary responsibility for the same control and require the user to resolve the conflict.

**System Component Inventory:**

- **FR-020**: System MUST allow users to create system components with: name, type (Person/Place/Thing), sub-type, description, owner, and status (Active/Planned/Decommissioned).
- **FR-021**: System MUST organize components into collapsible sections by type with item counts per section.
- **FR-022**: System MUST allow linking components to Security Capabilities (many-to-many relationship).
- **FR-023**: System MUST populate SSP Appendix A from the component inventory when generating system documentation.
- **FR-024**: System MUST flag Security Capabilities for review when a linked component is removed or decommissioned.

**Compliance Trends:**

- **FR-025**: System MUST capture compliance score snapshots daily at midnight UTC via a background hosted service, plus on-demand capture triggered by assessment completion events.
- **FR-026**: System MUST provide a time-series visualization of compliance scores with selectable granularity (daily, weekly, monthly, quarterly) and date range.
- **FR-027**: System MUST support multi-system trend comparison on a single chart with distinguishable series per system.
- **FR-028**: System MUST visually highlight significant score declines (drops > 5% between consecutive data points).

**Dashboard API:**

- **FR-029**: System MUST expose a portfolio summary endpoint returning aggregated metrics for all systems accessible to the authenticated user.
- **FR-030**: System MUST expose a system detail endpoint returning RMF phase progress, control family heatmap data, key metrics, and recent activity.
- **FR-031**: System MUST expose endpoints for Security Capability CRUD operations with search and filtering.
- **FR-032**: System MUST expose a gap analysis endpoint comparing capability coverage against the system's baseline.
- **FR-033**: System MUST expose a trend data endpoint returning time-series compliance snapshots for a given system and date range.
- **FR-034**: All dashboard API endpoints MUST enforce the same role-based access control as existing compliance tools — users only see systems they have role assignments for.
- **FR-035**: All dashboard API endpoints MUST support the existing pagination scheme (cursor-based, max page size 100).

### Key Entities

- **SecurityCapability**: An organization-wide reusable security measure (e.g., "MFA via Entra ID") shared across all registered systems. Attributes: name, provider, category, description, implementation status, owner. Maps to many controls across any system; maps to many components. Not scoped to a single system — a single capability can be mapped to controls on multiple systems simultaneously.
- **CapabilityControlMapping**: Join entity linking a SecurityCapability to a NistControl with a role (Primary/Supporting/Shared) and optional system scope. Enables the "write once, apply everywhere" narrative propagation.
- **SystemComponent**: An element of the system inventory categorized as Person (role/personnel), Place (data center/cloud region/facility), or Thing (tool/application/service). Attributes: name, type, sub-type, description, owner, status. Links to SecurityCapabilities and to a RegisteredSystem.
- **ComplianceTrendSnapshot**: A point-in-time record of a system's compliance metrics. Attributes: system reference, timestamp, compliance score, open finding counts by severity, open/overdue POA&M counts. Used for time-series trend visualization.
- **DashboardActivity**: A denormalized recent-event record for fast dashboard rendering. Attributes: system reference, event type, timestamp, actor, summary text, related entity reference.

## Assumptions

- The existing `RegisteredSystem` entity with `ComplianceScore`, `CurrentRmfStep`, and `AuthorizationDecision` (with expiration) provides the data needed for portfolio view. No changes to these entities are required.
- The existing `ControlImplementation` entity (which holds per-control narratives) will gain a nullable foreign key to `SecurityCapability` to track which capability generated the narrative.
- Narrative auto-generation from capabilities uses a template-based approach (not AI generation) by default, with AI-assisted narrative enhancement available as an optional enrichment step.
- All NIST SP 800-53 references target **Revision 5**, which includes all 20 control families (AC, AT, AU, CA, CM, CP, IA, IR, MA, MP, PE, PL, PM, PS, PT, RA, SA, SC, SI, SR).
- The dashboard is a standalone project (its own codebase, build pipeline, and deployment unit), separate from the Chat App. It consumes the MCP server and dashboard API endpoints over HTTP.
- Compliance trend snapshots are captured by a background service, not computed on-the-fly at query time, to ensure dashboard performance.
- The RBAC model governing who can see which systems (via `RmfRoleAssignment`) already enforces access control. Dashboard API endpoints inherit this same authorization logic.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can assess the compliance status of all their systems within 5 seconds of opening the dashboard, without issuing any chat commands.
- **SC-002**: Updating a Security Capability that maps to 50+ controls propagates narrative changes to all mapped controls within 10 seconds.
- **SC-003**: The gap analysis report identifies 100% of unmapped controls in the selected baseline with zero false positives (a control marked as "covered" has at least one mapped capability).
- **SC-004**: Dashboard views render within 2 seconds for portfolios of up to 50 systems and system detail views with 400+ controls.
- **SC-005**: The control family heatmap accurately reflects assessment data — compliance percentages match what would be computed by manually counting passed/failed controls per family.
- **SC-006**: Compliance trend charts display accurate historical data with data points matching the values in stored snapshots (no interpolation errors).
- **SC-007**: SSP Appendix A generated from the component inventory includes 100% of active components with correct type classifications.
- **SC-008**: 80% of ISSM/ISSO users can locate their system's compliance score and ATO expiration within 10 seconds of first visit (measured by click-tracking or user testing).
