# Feature Specification: Implementation Roadmap

**Feature Branch**: `031-implementation-roadmap`  
**Created**: 2025-03-15  
**Status**: Draft  
**Input**: User description: "Implementation Roadmap — Turn gaps into a sequenced, prioritized action plan with estimated effort. Integrates with M365 Teams chat and the Visual Compliance Dashboard."

## Clarifications

### Session 2026-03-15

- Q: Who should be able to generate, edit, and view roadmaps? → A: ISSM-only for generate/edit; ISSO, Engineer, AO can view (read-only)
- Q: How should controls be grouped into phases? → A: AI-driven clustering based on control relationships and complexity, with manual restructuring allowed post-generation
- Q: What should the effort estimation fallback be when no historical data exists? → A: AI estimates from control complexity and NIST guidance; historical task data refines estimates when available but is not required
- Q: How should risk reduction be calculated per phase? → A: Weighted severity score — each gap contributes points by severity (CAT I=10, CAT II=5, CAT III=1); phase reduction = sum of closed points / total points
- Q: Should the roadmap be exportable for offline use? → A: PDF export with timeline, phase tables, and risk curve for AO briefings

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate a Phased Roadmap from Gap Analysis (Priority: P1)

An ISSM reviews the Gap Analysis for a system and sees 47 unmapped or partially implemented controls. Instead of manually triaging each gap, the ISSM asks the system to generate an implementation roadmap. The system analyzes all gaps, groups them into logical phases based on severity, dependency chains, and control family relationships, estimates effort per control, projects cumulative risk reduction per phase, and returns a multi-phase action plan.

**Why this priority**: This is the core value proposition — transforming a flat list of gaps into an actionable, sequenced plan. Without this, the feature has no purpose.

**Independent Test**: Can be fully tested by running gap analysis on a system with a selected baseline and unmapped controls, then requesting a roadmap. Delivers a phased plan with effort estimates and risk projections.

**Acceptance Scenarios**:

1. **Given** a system with a selected baseline and at least one unmapped control, **When** the ISSM requests "Generate an implementation roadmap for [System]", **Then** the system produces a roadmap with one or more phases, each containing grouped controls, estimated effort in person-days, a target timeline, and projected risk reduction percentage.
2. **Given** a system with gaps spanning multiple severity levels, **When** a roadmap is generated, **Then** CAT I / critical-severity gaps are placed in the earliest phase, followed by infrastructure controls, operational controls, and audit/documentation controls.
3. **Given** a system with control dependencies (e.g., AC-2 Account Management should precede AU-6 Audit Review), **When** a roadmap is generated, **Then** dependent controls are sequenced after their prerequisites within the same or later phase.
4. **Given** a system with zero gaps (100% coverage), **When** a roadmap is requested, **Then** the system returns a response indicating no roadmap is needed — all controls are covered.

---

### User Story 2 - View Roadmap in the Dashboard (Priority: P1)

An ISSM or AO navigates to a system's detail page in the Visual Compliance Dashboard and clicks through to the Roadmap view. They see a timeline visualization showing phases as horizontal bars, a risk reduction curve projecting how risk decreases over time, and expandable phase tables listing each control with its effort estimate, assigned role, dependencies, and current status.

**Why this priority**: The dashboard is the primary visual interface for leadership. Displaying the roadmap here provides at-a-glance strategic planning visibility that directly supports authorization decisions.

**Independent Test**: Can be tested by generating a roadmap for a system, then navigating to the dashboard's roadmap page and verifying the timeline, risk curve, and phase detail tables render correctly with live data.

**Acceptance Scenarios**:

1. **Given** a system with a saved roadmap, **When** the user navigates to `/systems/:id/roadmap` in the dashboard, **Then** summary metric cards display total gaps, estimated total effort, projected risk reduction, and target completion timeframe.
2. **Given** a saved roadmap with multiple phases, **When** the roadmap page loads, **Then** a timeline visualization shows phases as horizontal bars positioned along a week-based axis with progress fill based on completion status.
3. **Given** a saved roadmap, **When** the user views the risk reduction section, **Then** a line chart shows the projected cumulative risk reduction curve from week 1 through the final phase.
4. **Given** a saved roadmap, **When** the user expands a phase, **Then** a table lists each control item with columns for Control ID, Gap Type, Estimated Effort, Assigned Role, Dependencies, and Status.

---

### User Story 3 - View Roadmap via M365 Teams Chat (Priority: P1)

An ISSM working in Microsoft Teams asks the ATO Copilot bot to generate or view an implementation roadmap for a system. The bot returns an Adaptive Card summarizing the roadmap with phase names, timelines, control counts, effort estimates, risk reduction projections, and action buttons to create a Kanban board or drill into phase details.

**Why this priority**: The ISSM persona operates primarily through Teams. Roadmap generation and viewing must work through the conversational interface to be usable in the ISSM's natural workflow.

**Independent Test**: Can be tested by sending the roadmap generation command in Teams and verifying the Adaptive Card renders with all expected sections and action buttons.

**Acceptance Scenarios**:

1. **Given** a system with gaps, **When** the ISSM sends "Generate an implementation roadmap for [System]" in Teams, **Then** an Adaptive Card is returned showing total gaps, number of phases, projected risk reduction percentage, and estimated total effort.
2. **Given** a generated roadmap, **When** the Adaptive Card is displayed, **Then** each phase is listed with its name, timeline (e.g., "Wk 1-2"), control count, effort in person-days, and risk reduction contribution.
3. **Given** a roadmap Adaptive Card, **When** the ISSM clicks "Create Kanban Board", **Then** a remediation Kanban board is created with phases mapped to task groupings and individual controls mapped to tasks.
4. **Given** a generated roadmap, **When** the ISSM sends "Show Phase 1 details for [System]'s roadmap", **Then** a detailed card lists each control in that phase with effort, assigned role, gap type, and dependency information.

---

### User Story 4 - Bridge Roadmap to Kanban Execution (Priority: P2)

After reviewing the implementation roadmap, the ISSM decides to convert it into an actionable Kanban board. The system creates a remediation board pre-populated with tasks derived from roadmap items, preserving phase groupings, effort estimates, role assignments, and dependency ordering. As engineers complete Kanban tasks, the roadmap's live status updates automatically.

**Why this priority**: Without the bridge to execution, the roadmap is a planning artifact only. Connecting it to the existing Kanban system closes the loop between strategy and action.

**Independent Test**: Can be tested by generating a roadmap, clicking "Create Kanban Board", and verifying tasks are created with correct control IDs, effort, and assignments. Then moving a task to Done and confirming the roadmap phase progress updates.

**Acceptance Scenarios**:

1. **Given** a saved roadmap, **When** the user triggers "Create Kanban Board from Roadmap", **Then** a remediation board is created with one task per roadmap item, each task populated with control ID, estimated effort, assigned role, and phase reference.
2. **Given** a roadmap-linked Kanban board, **When** a task is moved to Done, **Then** the corresponding roadmap item's status updates to Complete and the phase's progress percentage recalculates.
3. **Given** a roadmap where all items in Phase 1 are linked to completed Kanban tasks, **When** the roadmap is viewed, **Then** Phase 1 shows 100% complete and the actual risk reduction is compared against the projected reduction.
4. **Given** a roadmap-linked Kanban board already exists for a system, **When** the user attempts to create another board from the same roadmap, **Then** the system warns that a linked board already exists and offers to update it instead.

---

### User Story 5 - Track Roadmap Progress Over Time (Priority: P2)

The ISSM periodically checks roadmap progress to report to the AO. The system shows actual completion against the planned timeline, compares projected risk reduction against actual risk reduction (derived from assessment data), and highlights phases that are behind schedule.

**Why this priority**: Ongoing visibility into plan execution is essential for leadership reporting and course-correction, but depends on the core roadmap (US1) and Kanban bridge (US4) being in place first.

**Independent Test**: Can be tested by generating a roadmap, completing some Kanban tasks, and verifying the progress view shows actual-vs-planned metrics.

**Acceptance Scenarios**:

1. **Given** a roadmap with linked Kanban tasks partially completed, **When** the ISSM requests "Show roadmap progress for [System]", **Then** the response includes per-phase completion percentage, overall completion percentage, and items completed vs. total.
2. **Given** a roadmap where Phase 1's target completion date has passed, **When** progress is viewed, **Then** Phase 1 is flagged as behind schedule if not 100% complete, with the number of days overdue.
3. **Given** a roadmap with at least one completed phase, **When** progress is viewed in the dashboard, **Then** a dual-line chart shows projected risk reduction alongside actual risk reduction derived from the latest compliance score data.

---

### User Story 6 - Update and Reassign Roadmap Items (Priority: P3)

The ISSM needs to adjust the roadmap after an initial review — reordering phases, reassigning controls to different roles, splitting a large phase into two, or adjusting effort estimates based on team feedback. Changes propagate to linked Kanban tasks.

**Why this priority**: Roadmaps are living documents that require adjustment. This is important but lower priority because the initial generation and execution bridge deliver the primary value.

**Independent Test**: Can be tested by generating a roadmap, then updating a phase's order or reassigning a control item, and verifying the changes persist and propagate to linked Kanban tasks.

**Acceptance Scenarios**:

1. **Given** a saved roadmap, **When** the ISSM sends "Move SC-7 from Phase 2 to Phase 1 on [System]'s roadmap", **Then** the control is reassigned to Phase 1 and the phase's effort and risk projections recalculate.
2. **Given** a roadmap with a linked Kanban board, **When** a roadmap item's assigned role is changed, **Then** the corresponding Kanban task's assignee is updated to reflect the new role.
3. **Given** a saved roadmap, **When** the ISSM requests "Update effort for AC-2 to 6 person-days on [System]'s roadmap", **Then** the effort estimate updates and the parent phase's total effort recalculates.

---

### Edge Cases

- What happens when a system has gaps but no baseline selected? The system returns an error indicating a baseline must be selected before a roadmap can be generated.
- What happens when a roadmap is generated for a system with only inherited controls (zero customer-responsible gaps)? The system returns a response indicating all controls are covered through inheritance — no roadmap is needed.
- What happens when a Kanban task linked to a roadmap item is deleted? The roadmap item's status reverts to "Not Started" and the link is cleared, with a warning logged.
- What happens when the same system has multiple roadmaps? The system maintains a history of roadmaps with timestamps, but only one can be "Active" at a time. Previous roadmaps are archived and viewable for comparison.
- What happens when new gaps appear after a roadmap is generated (e.g., baseline re-tailored)? The system detects untracked gaps and prompts the user to regenerate or amend the roadmap.
- What happens when a phase has zero items after edits remove all controls from it? The empty phase is automatically removed and subsequent phases are renumbered.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST generate a multi-phase implementation roadmap from a system's gap analysis data, grouping unmapped, partially implemented, and unassessed controls into sequenced phases.
- **FR-002**: System MUST use AI-driven clustering to group controls into phases based on control relationships, complexity, severity, and dependency chains. CAT I / critical gaps MUST be weighted toward the earliest phases. The ISSM may manually restructure the generated phases (reorder, merge, split, move items between phases) after generation. The AI typically produces 3–5 phases; no hard cap is enforced.
- **FR-003**: System MUST sequence controls within and across phases based on dependency relationships (e.g., AC-2 before AU-6).
- **FR-004**: System MUST estimate effort in person-days for each control item using AI analysis of control complexity and NIST guidance as the primary method. When historical task completion data from past Kanban tasks is available, the system MUST use it to refine estimates. Historical data is not required — AI-only estimates MUST be usable on day one.
- **FR-005**: System MUST project cumulative risk reduction percentage at each phase milestone using a weighted severity scoring model: each gap contributes points based on severity (CAT I = 10 points, CAT II = 5 points, CAT III = 1 point). Phase risk reduction = (sum of points for gaps closed in that phase) / (total points across all gaps) × 100%. Cumulative risk reduction across all phases sums to 100% when all gaps are addressed.
- **FR-006**: System MUST assign a default responsible role (ISSO, Engineer, or ISSM) to each roadmap item based on the control family and gap type.
- **FR-007**: System MUST persist roadmaps with generation history — previous roadmaps are archived when a new one becomes Active — allowing only one "Active" roadmap per system at a time. In-place edits to the active roadmap increment the Version counter and update the UpdatedAt timestamp but do not create individual edit-level audit entries.
- **FR-008**: System MUST render roadmaps in the Visual Compliance Dashboard as a dedicated page with timeline visualization, risk reduction curve, and expandable phase detail tables.
- **FR-009**: System MUST render roadmaps in M365 Teams chat as Adaptive Cards with phase summaries, effort totals, risk projections, and action buttons.
- **FR-010**: System MUST support creating a Kanban remediation board pre-populated from a roadmap, mapping phases to task groupings and items to tasks.
- **FR-011**: System MUST maintain bi-directional sync between roadmap item status and linked Kanban task status — completing a Kanban task updates the roadmap item, and vice versa.
- **FR-012**: System MUST allow the ISSM to update roadmap items — reassign controls between phases, change role assignments, adjust effort estimates, and restructure phases (reorder, merge, split) — with changes propagating to linked Kanban tasks.
- **FR-013**: System MUST display roadmap progress showing actual completion vs. planned timeline with overdue phase highlighting.
- **FR-014**: System MUST compare projected risk reduction against actual risk reduction when assessment score data is available.
- **FR-015**: System MUST return a clear message when roadmap generation is requested for a system with no gaps or no baseline selected.
- **FR-016**: System MUST detect when new gaps appear that are not tracked in the current active roadmap and notify the user.
- **FR-017**: System MUST enforce RBAC on roadmap operations — only the ISSM (Compliance.SecurityLead) role may generate, edit, or delete roadmaps; ISSO (Compliance.Analyst), Engineer (Compliance.PlatformEngineer), and AO (Compliance.AuthorizingOfficial) roles have read-only access. Unauthorized attempts MUST return 403 Forbidden.
- **FR-018**: System MUST support exporting a roadmap as a PDF document containing the timeline visualization, phase detail tables (control ID, gap type, effort, role, status), and risk reduction curve, suitable for AO briefings and authorization package supplements.

### Key Entities

- **ImplementationRoadmap**: A versioned, phased action plan for closing compliance gaps on a specific system. Key attributes: system reference, status (Draft/Active/Completed/Archived), total estimated effort, projected risk reduction, creation timestamp, linked Kanban board reference.
- **RoadmapPhase**: A logical grouping of related controls within a roadmap, executed in sequence. Key attributes: phase name, display order, estimated total effort, target completion date, projected risk reduction contribution, status (NotStarted/InProgress/Complete).
- **RoadmapItem**: An individual control gap assigned to a phase. Key attributes: control identifier, gap type (Unmapped/PartiallyImplemented/NotAssessed), severity level (Critical/High/Medium), estimated effort in person-days, assigned role, dependency references, status, linked Kanban task reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An ISSM can generate a complete implementation roadmap from gap analysis in under 30 seconds for a system with up to 325 baseline controls.
- **SC-002**: 100% of gaps identified in gap analysis are accounted for in the generated roadmap — no gaps are dropped or missed.
- **SC-003**: Generated phases are correctly sequenced such that no control with a dependency appears in an earlier phase than its prerequisite.
- **SC-004**: Roadmap-to-Kanban conversion creates tasks for all roadmap items within 10 seconds, with zero data loss (control ID, effort, role all preserved).
- **SC-005**: Roadmap progress accurately reflects linked Kanban task status within one polling cycle (30 seconds) of a task status change.
- **SC-006**: The dashboard roadmap page loads and renders all visualizations (timeline, risk curve, phase tables) within 3 seconds for a system with up to 4 phases and 100 items.
- **SC-007**: The M365 Teams Adaptive Card for a roadmap displays correctly on desktop and mobile Teams clients with all action buttons functional.
- **SC-008**: Risk reduction projections are within 15% of actual post-remediation compliance score improvements, as measured after all items in a phase are completed. This is a post-deployment metric tracked via ConMon reports, not an automated test gate.

## Assumptions

- The existing gap analysis feature (Feature 030, US4) provides the input data — specifically the `GapAnalysisResponse` with per-family breakdown and unmapped control lists.
- The existing Kanban service (`IKanbanService`) and task enrichment service (`ITaskEnrichmentService`) are available for the roadmap-to-Kanban bridge and effort estimation.
- The existing M365 card router (`cardRouter.ts`) supports adding a new `roadmap` data type for Adaptive Card rendering.
- The existing dashboard infrastructure (React + Recharts + Tailwind) supports the new roadmap page without additional framework dependencies.
- Control dependency data is derived from NIST SP 800-53 control relationships and family ordering — not from a custom-maintained dependency graph.
- Effort estimation uses AI analysis of control complexity and NIST SP 800-53 guidance as the primary source. Historical task completion durations from the Kanban service refine estimates when available but are not required for initial roadmap generation.
- The ISSM persona has exclusive permission to generate, edit, and delete roadmaps (FR-017). ISSO, Engineer, and AO personas have read-only access. Unauthorized write attempts return 403 Forbidden, consistent with the existing RBAC model.

## Dependencies

- **Feature 030 (Visual Compliance Dashboard)**: Provides the dashboard infrastructure, gap analysis page, and API patterns for the new roadmap page.
- **Feature 002 (Remediation Kanban)**: Provides the Kanban board and task model for the roadmap-to-Kanban bridge.
- **Feature 012 (Task Enrichment)**: Provides AI-driven effort estimation and remediation script generation used for roadmap item enrichment.
- **Compliance Baseline Selection**: A system must have a selected baseline with gap analysis data before a roadmap can be generated.
